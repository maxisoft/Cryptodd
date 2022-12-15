import argparse
import collections
import concurrent.futures
import datetime
import functools
import logging
import sys
import tarfile
import time
from pathlib import Path
from typing import Iterator, Iterable


class ObDescriptor:
    name: str
    shape: tuple[int]
    columns: tuple[str]
    glob_pattern = '0*.mm.time'

    def __str__(self):
        return f'{type(self).__name__}({getattr(self, "name", "none")})'

    __repr__ = __str__

    def list_files(self, p: Path | Iterable[Path]) -> Iterator[Path]:
        if isinstance(p, Path):
            p = p.rglob(self.glob_pattern)
        for file in sorted(p, key=path_sort_by_name):
            parents = file.parents
            try:
                index = [x.name for x in file.parents].index(self.name)
            except ValueError:
                continue

            if index >= len(parents) - 3:
                continue

            if parents[index - 1].name != "ob":
                continue

            stem = file.stem.removesuffix('.mm')
            if not stem.startswith('0'):
                continue

            try:
                int(stem)
            except ValueError:
                continue

            mm_file = file.with_name(stem + '.mm')
            if not mm_file.exists():
                continue

            yield file

    def validate(self, file: Path, max_date: int = 0):
        assert file.name.endswith('.mm.time')
        stem = file.stem.removesuffix('.mm')

        try:
            int(stem)
        except ValueError:
            return False

        mm_file = file.with_name(stem + '.mm')
        if not mm_file.exists():
            return False

        try:
            stat = file.stat()
            mm_stat = mm_file.stat()
        except IOError:
            return False

        if max(stat.st_size, mm_stat.st_size) <= 0:
            return False

        num_entry = stat.st_size // sizeof_long

        if num_entry <= 0:
            return False

        if max_date > 0:
            return stat.st_mtime < max_date

        return True

    def archive_name(self, file: Path, ext='tar.xz'):
        parents = file.parents
        index = [x.name for x in file.parents].index(self.name)
        return '_'.join(
            tuple(x.name for x in reversed(parents[:index + 1])) + (file.stem.removesuffix('.mm'),)) + '.' + ext

    def archive_directory(self, file: Path):
        return self.name

    def compute_shape(self, num_elements: int) -> tuple[int]:
        return tuple(x if x != -1 else num_elements for x in self.shape)


descriptors: list['ObDescriptor'] = []


def register_descriptor_class(cls):
    if cls not in descriptors:
        descriptors.append(cls())
    return cls


sizeof_long = 64 // 8
sizeof_float = 32 // 8


def path_sort_by_name(p: Path):
    return p.name


_binance_columns = ('price', 'size', 'raw_size',
                    'mean_price', 'change_counter',
                    'total_change_counter', 'size_std',
                    'aggregate_count')


@register_descriptor_class
class BinanceObDescriptor(ObDescriptor):
    name = "Binance"
    shape = (-1, 128 * 2, len(_binance_columns))
    columns = _binance_columns


@register_descriptor_class
class BinanceFuturesObDescriptor(BinanceObDescriptor):
    name: str = "BinanceFutures"


@register_descriptor_class
class BitfinexObDescriptor(ObDescriptor):
    name = "Bitfinex"
    shape = (-1, 25 * 2, 3)
    columns = _binance_columns


@register_descriptor_class
class BitfinexP0ObDescriptor(ObDescriptor):
    name = "BitfinexP0"
    shape = (-1, 250 * 2, 3)
    columns = _binance_columns


parser = argparse.ArgumentParser()
parser.add_argument('--time', action='store', dest='time', default=datetime.timedelta(days=3).total_seconds(),
                    type=float)
parser.add_argument('-k', '--keep', action='store_true', dest='keep')
parser.add_argument('--fix', action='store_true', dest='fix')

parser.add_argument('rest', nargs=argparse.REMAINDER)

cancelled = False


def _process(descriptor: ObDescriptor, file_dir: Path, files: list[Path], args: argparse.Namespace):
    files.sort(key=path_sort_by_name)

    now = time.time()
    processed = 0
    processed_files = set()
    archive: tarfile.TarFile | None = None
    archive_name = ""
    unlink = False
    try:
        for file in files:
            if cancelled:
                break
            if not descriptor.validate(file, now - args.time):
                continue

            if archive is None:
                archive_name = descriptor.archive_name(file)
                archive_dir = Path(descriptor.archive_directory(file))
                archive_dir.mkdir(exist_ok=True)
                archive_name = archive_dir / archive_name
                archive = tarfile.open(archive_name, 'x:xz')

            stem = file.stem.removesuffix('.mm')
            mm_file = file.with_name(stem + '.mm')

            if not mm_file.exists():
                continue

            if args.fix:
                stable = False
                while not stable:
                    size = file.stat().st_size
                    num_elements = size // sizeof_long
                    mm_size = mm_file.stat().st_size
                    shape = descriptor.compute_shape(num_elements)
                    total_floats = functools.reduce(int.__mul__, shape)
                    mm_sub_shape = functools.reduce(int.__mul__, shape[1:], 1)
                    mm_num_elements = mm_size // (mm_sub_shape * sizeof_float)
                    if size % sizeof_long != 0:
                        print(f'fixing {file} alignment')
                        with open(file, 'rb+') as f:
                            size = size // sizeof_long * sizeof_long
                            f.truncate(size)
                            continue
                    if mm_size % (mm_sub_shape * sizeof_float) != 0:
                        print(f'fixing {mm_file} alignment')
                        with open(mm_file, 'rb+') as f:
                            mm_size = mm_size // (mm_sub_shape * sizeof_float) * (mm_sub_shape * sizeof_float)
                            f.truncate(mm_size)
                            continue
                    elif mm_size % (total_floats * sizeof_float) != 0:
                        print(f'fixing {mm_file} alignment')
                        with open(mm_file, 'rb+') as f:
                            mm_size = mm_size // (total_floats * sizeof_float) * (total_floats * sizeof_float)
                            f.truncate(mm_size)
                            continue
                    elif num_elements < mm_num_elements:
                        print(f'fixing {file} alignment')
                        with open(file, 'rb+') as f:
                            size = mm_num_elements * sizeof_long
                            f.truncate(size)
                            continue
                    else:
                        stable = True
                        break

            archive.add(file, arcname=file.name, recursive=False)
            archive.add(mm_file, arcname=mm_file.name, recursive=False)
            processed += 1
            processed_files.add(file)
            processed_files.add(mm_file)
        return Path(archive.name) if archive is not None else archive_name, processed_files
    except Exception:
        unlink = True
        raise
    finally:
        if archive is not None:
            archive.close()
            if processed == 0 or unlink or cancelled:
                if archive.name:
                    Path(archive.name).unlink(missing_ok=True)


if __name__ == '__main__':
    args = parser.parse_args(sys.argv[1:])
    jobs = collections.defaultdict(list)
    for path in (args.rest or (Path(),)):
        files = sorted(Path(path).rglob(ObDescriptor.glob_pattern), key=path_sort_by_name)
        for descriptor in descriptors:
            for file in descriptor.list_files(files):
                jobs[descriptor, file.parent].append(file)

    print("there's %d jobs" % len(jobs))
    tasks = []
    with concurrent.futures.ThreadPoolExecutor() as pool:
        try:

            for (descriptor, file_dir), files in jobs.items():
                fut = pool.submit(_process, descriptor, file_dir, files, args)
                tasks.append(fut)

            for fut in concurrent.futures.as_completed(tasks):
                try:
                    archive, files = fut.result()
                except Exception as e:
                    print(e, list(jobs.keys())[tasks.index(fut)], file=sys.stderr)
                else:
                    if not args.keep:
                        for file in files:
                            logging.debug('unlinking %s', file)
                            Path(file).unlink(missing_ok=True)
                    if archive:
                        print(f'created {archive}')
        except KeyboardInterrupt:
            cancelled = True
            for fut in tasks:
                if not fut.done():
                    fut.cancel()

            raise
