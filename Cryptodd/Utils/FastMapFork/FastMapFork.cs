/* Fork of https://github.com/Wsm2110/Faster.Map/blob/main/FastMap.cs
 Containing number of importants fixes
 Allowing one to use ref
*/

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Faster.Map.Core;

namespace Cryptodd.Utils.FastMapFork
{
    public struct FastEntry<TKey, TValue>
    {
        public TKey Key;

        public TValue Value;
    }

    /// <summary>
    /// This hashmap uses the following
    /// - Open addressing
    /// - Uses linear probing
    /// - Robinghood hashing
    /// - Upper limit on the probe sequence lenght(psl) which is Log2(size)
    /// - Keeps track of the currentProbeCount which makes sure we can back out early eventhough the maxprobcount exceeds the cpc
    /// - loadfactor can easily be increased to 0.9 while maintaining an incredible speed
    /// - use numerical values as keys
    /// - fibonacci hashing
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    public class FastMapFork<TKey, TValue> where TKey : struct, IEquatable<TKey>
    {
        #region Properties

        /// <summary>
        /// Gets or sets how many elements are stored in the map
        /// </summary>
        /// <value>
        /// The entry count.
        /// </value>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the size of the map
        /// </summary>
        /// <value>
        /// The size.
        /// </value>
        public uint Capcity => (uint)_entries.Length;

        public ref struct Enumerator
        {
            private readonly Span<InfoByte> _info;
            private readonly Span<FastEntry<TKey, TValue>> _entries;

            /// <summary>The next index to yield.</summary>
            private int _index;

            /// <summary>Initialize the enumerator.</summary>
            /// <param name="dict">The dict to enumerate.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(FastMapFork<TKey, TValue> dict)
            {
                _info = dict._info;
                _entries = dict._entries;
                _index = dict._entries.Length;
            }

            /// <summary>Advances the enumerator to the next element of the dict.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                var index = _index - 1;
                while (index >= 0 && _info[index].IsEmpty())
                {
                    index -= 1;
                }

                if (index <= 0)
                {
                    return false;
                }

                _index = index;
                return true;
            }

            /// <summary>Gets the element at the current position of the enumerator.</summary>
            public readonly ref FastEntry<TKey, TValue> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _entries[_index];
            }
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        #endregion

        #region Fields

        private InfoByte[] _info;
        private FastEntry<TKey, TValue>[] _entries;
        private uint _length;
        private readonly double _loadFactor;
        private const uint GoldenRatio = 0x9E3779B9; //2654435769;
        private int _shift = 32;
        private uint _maxProbeSequenceLength;
        private byte _currentProbeSequenceLength;
        private uint _maxlength;

        #endregion

        #region Constructor

        public const double DefaultLoadFactor = 0.8d;

        /// <summary>
        /// Initializes a new instance of the <see cref="FastMapForkFork{TKey,TValue}"/> class.
        /// </summary>
        public FastMapFork() : this(8) { }

        /// <summary>
        /// Initializes a new instance of class.
        /// </summary>
        /// <param name="length">The length of the hashmap. Will always take the closest power of two</param>
        /// <param name="loadFactor">The loadfactor determines when the hashmap will resize(default is 0.5d) i.e size 32 loadfactor 0.5 hashmap will resize at 16</param>
        public FastMapFork(uint length, double loadFactor = DefaultLoadFactor)
        {
            //default length is 8
            _length = length == 0 ? 8 : length;
            _loadFactor = loadFactor;

            var size = NextPow2(_length);
            _maxProbeSequenceLength = _loadFactor <= 0.5 ? Log2(_length) : PslLimit(_length);
            Debug.Assert(_maxProbeSequenceLength <= sbyte.MaxValue);

            _maxlength = (uint)(size * loadFactor);

            _shift = _shift - Log2(_length) + 1;
            _entries = new FastEntry<TKey, TValue>[size + _maxProbeSequenceLength + 1];
            _info = new InfoByte[size + _maxProbeSequenceLength + 1];
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Inserts a value using a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        [MethodImpl(256)]
        public bool TryEmplace(in TKey key, in TValue value)
        {
            //Resize if loadfactor is reached
            if (Count >= _maxlength)
            {
                Resize();
            }

            //Get object identity hashcode
            var hashcode = key.GetHashCode();

            // Objectidentity hashcode * golden ratio (fibonnachi hashing) followed by a
            uint index = ((uint)hashcode * GoldenRatio) >> _shift;

            //check if key is unique
            if (ContainsKey(in key, index))
            {
                return false;
            }

            //Create default info byte
            InfoByte current = default;

            //Assign 0 to psl so it wont be seen as empty
            current.Psl = 0;

            //retrieve infobyte
            ref var info = ref _info[index];

            //Increase _current probe sequence
            if (_currentProbeSequenceLength < current.Psl)
            {
                _currentProbeSequenceLength = current.Psl;
            }

            //Empty spot, add entry
            if (info.IsEmpty())
            {
                ref var entry = ref _entries[index];
                entry.Key = key;
                entry.Value = value;
                info = current;
                ++Count;
                return true;
            }

            //Create entry
            FastEntry<TKey, TValue> fastEntry = default;
            fastEntry.Value = value;
            fastEntry.Key = key;

            do
            {
                //Increase _current probe sequence
                if (_currentProbeSequenceLength < current.Psl)
                {
                    _currentProbeSequenceLength = current.Psl;
                }

                //Empty spot, add entry
                if (info.IsEmpty())
                {
                    _entries[index] = fastEntry;
                    info = current;
                    ++Count;
                    return true;
                }

                //Steal from the rich, give to the poor
                if (current.Psl > info.Psl)
                {
                    Swap(ref fastEntry, ref _entries[index]);
                    Swap(ref current, ref info);
                    continue;
                }

                //max psl is reached, resize
                if (current.Psl >= _maxProbeSequenceLength)
                {
                    ++Count;
                    Resize();
                    EmplaceInternal(ref fastEntry, ref current);
                    return true;
                }

                //increase index
                info = ref _info[++index];

                //increase probe sequence length
                current.Psl = (byte) Math.Min((uint) current.Psl + 1, _maxProbeSequenceLength);
            } while (true);
        }

        /// <summary>
        /// Gets the entry with the corresponding key
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="defaultValue">The entry to returns if not found.</param>
        /// <returns></returns>
        [MethodImpl(256)]
        public ref FastEntry<TKey, TValue> Get(in TKey key, ref FastEntry<TKey, TValue> defaultValue)
        {
            //Get object identity hashcode
            var hashcode = key.GetHashCode();

            // Objectidentity hashcode * golden ratio (fibonnachi hashing) followed by a shift
            uint index = ((uint)hashcode * GoldenRatio) >> _shift;

            //Determine max distance
            var maxDistance = index + _currentProbeSequenceLength;

            do
            {
                //Get entry by ref
                ref var info = ref _info[index];

                if (info.IsEmpty())
                {
                    break;
                }
                
                ref var entry = ref _entries[index];

                if (IsKeyMatching(in key, in entry.Key))
                {
                    return ref entry;
                }

                ++index;
                //increase index by one and validate if within bounds
            } while (index <= maxDistance);

            return ref defaultValue;
        }

        /// <summary>
        /// Update entry using a key and value
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        [MethodImpl(256)]
        public bool Update(in TKey key, in TValue value)
        {
            //Get object identity hashcode
            var hashcode = key.GetHashCode();

            //Objectidentity hashcode * golden ratio (fibonnachi hashing) followed by a shift
            uint index = (uint)hashcode * GoldenRatio >> _shift;

            //Determine max distance
            var maxDistance = index + _currentProbeSequenceLength;

            do
            {
                //unrolling loop twice seems to give a minor speedboost
                ref var info = ref _info[index];
                
                if (info.IsEmpty())
                {
                    return false;
                }
                
                ref var entry = ref _entries[index];

                if (IsKeyMatching(in key, in entry.Key))
                {
                    entry.Value = value;
                    return true;
                }

                //increase index by one and validate if within bounds
            } while (++index <= maxDistance);

            //entry not found
            return false;
        }

        /// <summary>
        /// Removes entry using a backshift removal
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Operation succeeded yes or no</returns>
        [MethodImpl(256)]
        public bool Remove(in TKey key)
        {
            //Get ObjectIdentity hashcode
            int hashcode = key.GetHashCode();

            //Objectidentity hashcode * golden ratio (fibonnachi hashing) followed by a shift
            uint index = (uint)hashcode * GoldenRatio >> _shift;

            //Determine max distance
            var maxDistance = index + _currentProbeSequenceLength;

            do
            {
                ref var entry = ref _entries[index];
                ref var info = ref _info[index];

                if (info.IsEmpty())
                {
                    return false;
                }

                if (IsKeyMatching(in key, in entry.Key))
                {
                    //remove entry
                    if (RuntimeHelpers.IsReferenceOrContainsReferences<FastEntry<TKey, TValue>>())
                    {
                        entry = default;
                    }

                    //remove infobyte
                    info = default;
                    //remove entry from list
                    --Count;
                    ShiftRemove(ref index);
                    return true;
                }

                //increase index by one and validate if within bounds
            } while (++index <= maxDistance);

            // No entries removed
            return false;
        }

        /// <summary>
        /// Determines whether the specified key exists in the hashmap
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>
        ///   <c>true</c> if the specified key contains key; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(256)]
        public bool Contains(in TKey key)
        {
            //Get ObjectIdentity hashcode
            int hashcode = key.GetHashCode();

            //Objectidentity hashcode * golden ratio (fibonnachi hashing) followed by a shift
            uint index = (uint)hashcode * GoldenRatio >> _shift;

            //Determine max distance
            var maxDistance = index + _currentProbeSequenceLength;

            do
            {
                //unrolling loop twice seems to give a minor speedboost
                ref var info = ref _info[index];
                if (info.IsEmpty())
                {
                    return false;
                }
                
                ref var entry = ref _entries[index];
                if (IsKeyMatching(in key, in entry.Key))
                {
                    return true;
                }

                //increase index by one and validate if within bounds
            } while (++index <= maxDistance);

            //not found
            return false;
        }

        /// <summary>
        /// Copies all entries from source to target
        /// </summary>
        /// <param name="fastMap">The fast map.</param>
        [MethodImpl(256)]
        public void Copy(FastMapFork<TKey, TValue> fastMap)
        {
            for (var i = 0; i < fastMap._entries.Length; i++)
            {
                var info = fastMap._info[i];
                if (info.IsEmpty())
                {
                    continue;
                }

                ref var entry = ref fastMap._entries[i];
                TryEmplace(in entry.Key, in entry.Value);
            }
        }

        /// <summary>
        /// Returns the current index of Tkey
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        [MethodImpl(256)]
        public int IndexOf(in TKey key)
        {
            //Get ObjectIdentity hashcode
            int hashcode = key.GetHashCode();

            //Objectidentity hashcode * golden ratio (fibonnachi hashing) followed by a shift
            uint index = (uint)hashcode * GoldenRatio >> _shift;
            
            for (int i = 0; i < _entries.Length; i++)
            {
                var indexSafe = (i + index) % _info.Length;
                var info = _info[indexSafe];
                if (info.IsEmpty())
                {
                    return -1;
                }

                if (IsKeyMatching(in _entries[indexSafe].Key, in key))
                {
                    return (int) indexSafe;
                }
            }

            //Return -1 which indicates not found
            return -1;
        }

        /// <summary>
        /// Set default state of all entries
        /// </summary>
        public void Clear()
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                _info[i] = default;
            }

            ((Span<FastEntry<TKey, TValue>>)_entries).Clear();

            Count = 0;
        }

        /// <summary>
        /// Gets or sets the entry by using a TKey
        /// </summary>
        /// <value>
        /// </value>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">Unable to find entry - {key.GetType().FullName} key - {key.GetHashCode()}
        /// or
        /// Unable to find entry - {key.GetType().FullName} key - {key.GetHashCode()}</exception>
        /// <exception cref="KeyNotFoundException">Unable to find entry - {key.GetType().FullName} key - {key.GetHashCode()}
        /// or
        /// Unable to find entry - {key.GetType().FullName} key - {key.GetHashCode()}</exception>
        public TValue this[in TKey key]
        {
            set
            {
                if (!Update(in key, in value))
                {
                    throw new KeyNotFoundException(
                        $"Unable to find entry - {key.GetType().FullName} key - {key.GetHashCode()}");
                }
            }
        }
        
        public bool EnsureCapacity(int capacity)
        {
            if (Capcity < capacity)
            {
                Resize((uint) capacity);
                return true;
            }

            return false;
        }

        #endregion

        

        #region Private Methods

        /// <summary>
        /// Shift remove will shift all entries backwards until there is an empty entry
        /// </summary>
        /// <param name="index">The index.</param>
        [MethodImpl(256)]
        private void ShiftRemove(ref uint index)
        {
            //Get next entry
            ref var next = ref _info[++index % _info.Length];

            while (!next.IsEmpty())
            {
                //decrease next psl by 1
                if (next.Psl > 0)
                {
                    next.Psl--;
                }
                //swap upper info with lower
                Swap(ref next, ref _info[(index - 1) % _info.Length]);
                //swap upper entry with lower
                Swap(ref _entries[index % _entries.Length], ref _entries[(index - 1) % _entries.Length]);
                //increase index by one
                next = ref _info[++index % _info.Length];
            }
        }

        /// <summary>
        /// Emplaces a new entry without checking for key existence
        /// </summary>
        /// <param name="entry">The fast entry.</param>
        /// <param name="current">The information byte.</param>
        [MethodImpl(256)]
        private void EmplaceInternal(ref FastEntry<TKey, TValue> entry, ref InfoByte current)
        {
            //get objectidentiy
            var hashcode = entry.Key.GetHashCode();

            uint index = ((uint)hashcode * GoldenRatio) >> _shift;

            //reset psl
            current.Psl = 0;

            ref var info = ref _info[index];

            do
            {
                if (info.IsEmpty())
                {
                    _entries[index] = entry;
                    info = current;
                    return;
                }

                if (current.Psl > info.Psl)
                {
                    Swap(ref entry, ref _entries[index]);
                    Swap(ref current, ref _info[index]);
                    continue;
                }

                _currentProbeSequenceLength = Math.Max(_currentProbeSequenceLength, current.Psl);

                //increase index
                info = ref _info[++index];

                //increase probe sequence length
                current.Psl = (byte) Math.Min(current.Psl + 1, _maxProbeSequenceLength);
            } while (true);
        }

        /// <summary>
        /// Swaps the content of the specified FastEntry values
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        [MethodImpl(256)]
        private static void Swap(ref FastEntry<TKey, TValue> x, ref FastEntry<TKey, TValue> y)
        {
            (x, y) = (y, x);
        }

        /// <summary>
        /// Swaps the content of the specified Infobyte values
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        [MethodImpl(256)]
        private static void Swap(ref InfoByte x, ref InfoByte y)
        {
            (x, y) = (y, x);
        }

        /// <summary>
        /// Returns a power of two probe sequence lengthzz
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns></returns>
        [MethodImpl(256)]
        private static uint PslLimit(uint size)
        {
            return size switch
            {
                16 => 4,
                32 => 5,
                64 => 6,
                128 => 7,
                256 => 8,
                512 => 9,
                1024 => 12,
                2048 => 15,
                4096 => 20,
                8192 => 25,
                16384 => 30,
                32768 => 35,
                65536 => 40,
                131072 => 45,
                262144 => 50,
                524288 => 55,
                1048576 => 60,
                2097152 => 65,
                4194304 => 70,
                8388608 => 75,
                16777216 => 80,
                33554432 => 85,
                67108864 => 90,
                134217728 => 95,
                268435456 => 100,
                536870912 => 105,
                _ => 10
            };
        }

        [MethodImpl(256)]
        private bool ContainsKey(in TKey key, uint index)
        {
            //Determine max distance
            var maxDistance = index + _currentProbeSequenceLength;

            do
            {
                //unrolling loop twice seems to give a minor speedboost
                ref var info = ref _info[index];

                if (info.IsEmpty())
                {
                    return false;
                }
                
                ref var entry = ref _entries[index];
                if (IsKeyMatching(in key, in entry.Key))
                {
                    return true;
                }


                //increase index by one and validate if within bounds
            } while (++index <= maxDistance);

            return false;
        }

        private void Resize() => Resize(_length);

        /// <summary>
        /// Resizes this instance.
        /// </summary>
        private void Resize(uint length)
        {
            _shift--;
            _length = NextPow2(length + 1);
            _maxProbeSequenceLength = _loadFactor <= 0.5 ? Log2(_length) : PslLimit(_length);
            Debug.Assert(_maxProbeSequenceLength <= sbyte.MaxValue);
            _maxlength = (uint)(_length * _loadFactor);

            var oldEntries = _entries;
            var oldInfo = _info;

            _entries = new FastEntry<TKey, TValue>[_length + _maxProbeSequenceLength + 1];
            _info = new InfoByte[_length + _maxProbeSequenceLength + 1];

            for (var i = 0; i < oldEntries.Length; ++i)
            {
                var info = oldInfo[i];
                if (info.IsEmpty())
                {
                    continue;
                }

                ref var entry = ref oldEntries[i];
                EmplaceInternal(ref entry, ref info);
            }
        }

        /// <summary>
        /// Calculates next power of 2
        /// </summary>
        /// <param name="c">The c.</param>
        /// <returns></returns>
        ///
        [MethodImpl(256)]
        private static uint NextPow2(uint c)
        {
            c--;
            c |= c >> 1;
            c |= c >> 2;
            c |= c >> 4;
            c |= c >> 8;
            c |= c >> 16;
            return ++c;
        }

        // Used for set checking operations (using enumerables) that rely on counting
        private static byte Log2(uint value) => unchecked((byte)BitOperations.Log2(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsKeyMatching(in TKey a, in TKey b) => a.Equals(b);

        #endregion
    }
}