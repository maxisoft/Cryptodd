import datetime
import lzma
import re
import shutil
import time

import numpy as np
import pandas as pd
import hashlib
import requests
import joblib
from pathlib import Path
from tqdm.auto import tqdm

#mkdir -p input &&  find -name "ftx_regrouped_orderbook_*.parquet" -type f -maxdepth 1 -size +16k -exec ln -s $PWD/{} "$PWD/input" \;

def hash_pair(pair: str, salt=18446744073709551557):
    d = hashlib.sha256(pair.encode('utf-8')).digest()
    res = salt
    for i in range(len(d) // 8):
        res ^= int.from_bytes(d[i * 8:(i + 1) * 8], 'big')
    res &= ~(1 << 63)
    return res


def build_pairs_hash_table(src=None):
    markets: dict = requests.get('https://ftx.com/api/markets').json()
    assert markets['success'] is True
    markets: set[str] = {p['name'] for p in markets['result']}
    res = src or dict()
    if res and set(res.keys()).issuperset(markets):
        return
    for market in markets:
        res[hash_pair(market)] = market
        if m := re.match(r'^\w+-(\d{4})$', market):
            for d in range(1, 32):
                for mo in range(1, 13):
                    mm = market.replace(m.group(1), f'{d:02d}{mo:02d}')
                    res[hash_pair(mm)] = mm

                    mm = market.replace(m.group(1), f'{mo:02d}{d:02d}')
                    res[hash_pair(mm)] = mm
    return res


def main():
    ht = None
    if Path('ht.pkl.xz').exists():
        ht = joblib.load('ht.pkl.xz')
    ht = build_pairs_hash_table(ht)
    joblib.dump(ht, 'ht.pkl.xz')

    assert hash_pair("BTC-PERP") == 7966697908167619883
    assert hash_pair("ETH-PERP") == 3846482641729352903
    p: Path

    files = sorted(filter(lambda p: p.is_file() or p.is_symlink(), Path('input').rglob('ftx_regrouped_orderbook_*.parquet')), key=lambda p: p.stem)
    processed_dir = Path('processed')
    processed_dir.mkdir(exist_ok=True)
    for p in tqdm(files):
        if abs(time.time() - p.resolve().stat().st_mtime) < 6 * 60 * 60:
            print(f'{p} mtime is too recent stopping ...')
            break
        if (processed_dir / p.name).exists():
            print(f'skipping {p} as it was already processed ...')
            continue
        m = re.match(r'ftx_regrouped_orderbook_(\d+)_(\d+)_(\d+)_(\d+)\.parquet', p.name)
        file_dt = datetime.datetime(year=int(m.group(1)), month=int(m.group(2)), day=int(m.group(3)), hour=int(m.group(4)), tzinfo=datetime.timezone.utc)
        if abs((file_dt - datetime.datetime.now().astimezone()).total_seconds()) < 6 * 60 * 60:
            print(f'{p} time is too recent stopping ...')
            break
        try:
            df = pd.read_parquet(p)
        except (TypeError):
            continue
        assert len(df.columns) == 102
        df.sort_values(['time'], kind='stable', inplace=True)
        df.set_index(['market', 'time'], inplace=True, drop=False, verify_integrity=True)
        for market in tqdm(df['market'].unique()):
            market_name = ht.get(market, market)
            market_name = str(market_name).replace('-', '_').replace('/', '_')
            target_path = Path('out', market_name)
            target_path.mkdir(parents=True, exist_ok=True)
            sub: pd.DataFrame = df.query(f'market == {market}')
            dt = datetime.datetime.utcfromtimestamp(sub['time'].iloc[0] / 1000)

            target = target_path / f'{dt.year:04d}_{dt.month:02d}.csv'
            dt.replace(minute=0, second=0)
            append = target.exists()
            sub.to_csv(target, mode='a' if append else 'w', header=not append, index=False, columns=[c for c in sub.columns if c != "market"])

            for i in range(1, 6):
                old_dt = dt - datetime.timedelta(days=33 * i)
                old_target = target_path / f'{old_dt.year:04d}_{old_dt.month:02d}.csv'
                if old_target.is_file():
                    #print('creating lzma file for %s' % old_target)
                    with lzma.open(old_target.with_name(old_target.name + '.xz'), 'wb', format=lzma.FORMAT_XZ) as lzf:
                        with old_target.open('rb') as f:
                            shutil.copyfileobj(f, lzf)

                    old_target.unlink()

        p.rename(processed_dir / p.name)


if __name__ == '__main__':

    main()
