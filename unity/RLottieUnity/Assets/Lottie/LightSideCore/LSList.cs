using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using LSCore.DataStructs;

namespace LSCore
{
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    public class LSList<T> :
        IList<T>,
        IList
    {
        [NonSerialized] public T[] items;
        private int size;
        private int version;
        [NonSerialized] private object syncRoot;
        private static readonly T[] emptyArray = Array.Empty<T>();

        public LSList() => items = emptyArray;

        public LSList(int capacity)
        {
            if (capacity == 0)
            {
                items = emptyArray;
            }
            else
            {
                items = new T[capacity];
            }
        }


        public LSList(IEnumerable<T> collection)
        {
            if (collection is ICollection<T> objs)
            {
                int count = objs.Count;
                if (count == 0)
                {
                    items = emptyArray;
                }
                else
                {
                    items = new T[count];
                    objs.CopyTo(items, 0);
                    size = count;
                }
            }
            else
            {
                size = 0;
                items = emptyArray;
                foreach (T obj in collection)
                {
                    Add(obj);
                }
            }
        }


        public int Capacity
        {
            get => items.Length;
            set
            {
                if (value == items.Length)
                {
                    return;
                }

                if (value > 0)
                {
                    T[] destinationArray = new T[value];
                    if (size > 0)
                    {
                        Array.Copy(items, 0, destinationArray, 0, size);
                    }

                    items = destinationArray;
                }
                else
                {
                    items = emptyArray;
                }
            }
        }
        
        public int Count => size;
        bool IList.IsFixedSize => false;
        bool ICollection<T>.IsReadOnly => false;
        bool IList.IsReadOnly => false;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot
        {
            get
            {
                if (syncRoot == null)
                {
                    Interlocked.CompareExchange<object>(ref syncRoot, new object(), null);
                }

                return syncRoot;
            }
        }

        public ref T this[int index] => ref items[index];

        T IList<T>.this[int index]
        {
            get => this[index];
            set => this[index] = value;
        }

        public ref T this[Index index]
        {
            get
            {
                int offset = index.GetOffset(size);
                return ref items[offset];
            }
        }
        
        public ArraySlice<T> this[Range range]
        {
            get
            {
                var count = Count;
                var start = range.Start.GetOffset(count);
                var end = range.End.GetOffset(count);
                count = end - start;
                return new ArraySlice<T>(items, start, count);
            }
        }
        
        private static bool IsCompatibleObject(object value)
        {
            if (value is T)
            {
                return true;
            }

            return value == null && default(T) == null;
        }


        object IList.this[int index]
        {
            get => this[index];
            set => this[index] = (T)value;
        }

        public bool TryGet(int index, out T value)
        {
            if (index < 0 || index >= size)
            {
                value = default;
                return false;
            }

            value = items[index];
            return true;
        }
        
        public void Add(in T item)
        {
            if (size == items.Length)
            {
                EnsureCapacity(size + 1);
            }
            
            items[size++] = item;
            ++version;
        }

        public void Add(T item)
        {
            if (size == items.Length)
            {
                EnsureCapacity(size + 1);
            }

            items[size++] = item;
            ++version;
        }


        int IList.Add(object item)
        {
            Add((T)item);
            return Count - 1;
        }


        public void AddRange(IEnumerable<T> collection) => InsertRange(size, collection);


        public ReadOnlyCollection<T> AsReadOnly() => new(this);


        public int BinarySearch(int index, int count, T item, IComparer<T> comparer)
        {
            return Array.BinarySearch(items, index, count, item, comparer);
        }


        public int BinarySearch(T item) => BinarySearch(0, Count, item, null);


        public int BinarySearch(T item, IComparer<T> comparer)
        {
            return BinarySearch(0, Count, item, comparer);
        }


        public void Clear()
        {
            if (size > 0)
            {
                Array.Clear(items, 0, size);
                size = 0;
            }

            ++version;
        }
        
        public void FakeClear()
        {
            size = 0;
            ++version;
        }


        public bool Contains(T item)
        {
            if (item == null)
            {
                for (int index = 0; index < size; ++index)
                {
                    if (items[index] == null)
                    {
                        return true;
                    }
                }

                return false;
            }

            EqualityComparer<T> equalityComparer = EqualityComparer<T>.Default;
            for (int index = 0; index < size; ++index)
            {
                if (equalityComparer.Equals(items[index], item))
                {
                    return true;
                }
            }

            return false;
        }


        bool IList.Contains(object item) => IsCompatibleObject(item) && Contains((T)item);

        public LSList<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
        {
            LSList<TOutput> outputList = new LSList<TOutput>(size);
            for (int index = 0; index < size; ++index)
            {
                outputList.items[index] = converter(items[index]);
            }

            outputList.size = size;
            return outputList;
        }


        public void CopyTo(T[] array) => CopyTo(array, 0);


        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            Array.Copy(items, 0, array, arrayIndex, size);
        }

        public void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            Array.Copy(items, index, array, arrayIndex, count);
        }


        public void CopyTo(T[] array, int arrayIndex)
        {
            Array.Copy(items, 0, array, arrayIndex, size);
        }

        private void EnsureCapacity(int min)
        {
            if (items.Length >= min)
            {
                return;
            }

            int num = items.Length == 0 ? 4 : items.Length * 2;
            if ((uint)num > 2146435071U)
            {
                num = 2146435071;
            }

            if (num < min)
            {
                num = min;
            }

            Capacity = num;
        }


        public bool Exists(Predicate<T> match) => FindIndex(match) != -1;


        public T Find(Predicate<T> match)
        {
            for (int index = 0; index < size; ++index)
            {
                if (match(items[index]))
                {
                    return items[index];
                }
            }

            return default;
        }


        public LSList<T> FindAll(Predicate<T> match)
        {
            LSList<T> all = new LSList<T>();
            for (int index = 0; index < size; ++index)
            {
                if (match(items[index]))
                {
                    all.Add(items[index]);
                }
            }

            return all;
        }


        public int FindIndex(Predicate<T> match) => FindIndex(0, size, match);


        public int FindIndex(int startIndex, Predicate<T> match)
        {
            return FindIndex(startIndex, size - startIndex, match);
        }


        public int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            int num = startIndex + count;
            for (int index = startIndex; index < num; ++index)
            {
                if (match(items[index]))
                {
                    return index;
                }
            }

            return -1;
        }


        public T FindLast(Predicate<T> match)
        {
            for (int index = size - 1; index >= 0; --index)
            {
                if (match(items[index]))
                {
                    return items[index];
                }
            }

            return default;
        }


        public int FindLastIndex(Predicate<T> match)
        {
            return FindLastIndex(size - 1, size, match);
        }


        public int FindLastIndex(int startIndex, Predicate<T> match)
        {
            return FindLastIndex(startIndex, startIndex + 1, match);
        }


        public int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            int num = startIndex - count;
            for (int lastIndex = startIndex; lastIndex > num; --lastIndex)
            {
                if (match(items[lastIndex]))
                {
                    return lastIndex;
                }
            }

            return -1;
        }


        public void ForEach(Action<T> action)
        {
            int version = this.version;
            for (int index = 0; index < size && (version == this.version); ++index)
            {
                action(items[index]);
            }
        }


        public Enumerator GetEnumerator() => new(this);


        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);


        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);


        public LSList<T> GetRange(int index, int count)
        {
            LSList<T> range = new LSList<T>(count);
            Array.Copy(items, index, range.items, 0, count);
            range.size = count;
            return range;
        }


        public int IndexOf(T item) => Array.IndexOf(items, item, 0, size);


        int IList.IndexOf(object item)
        {
            return IsCompatibleObject(item) ? IndexOf((T)item) : -1;
        }


        public int IndexOf(T item, int index)
        {
            return Array.IndexOf(items, item, index, size - index);
        }


        public int IndexOf(T item, int index, int count)
        {
            return Array.IndexOf(items, item, index, count);
        }


        public void Insert(int index, T item)
        {
            if (size == items.Length)
            {
                EnsureCapacity(size + 1);
            }

            if (index < size)
            {
                Array.Copy(items, index, items, index + 1, size - index);
            }

            items[index] = item;
            ++size;
            ++version;
        }


        void IList.Insert(int index, object item) => Insert(index, (T)item);

        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection is ICollection<T> objs)
            {
                int count = objs.Count;
                if (count > 0)
                {
                    EnsureCapacity(size + count);
                    if (index < size)
                    {
                        Array.Copy(items, index, items, index + count, size - index);
                    }

                    if (this == objs)
                    {
                        Array.Copy(items, 0, items, index, index);
                        Array.Copy(items, index + count, items, index * 2, size - index);
                    }
                    else
                    {
                        T[] array = new T[count];
                        objs.CopyTo(array, 0);
                        array.CopyTo(items, index);
                    }

                    size += count;
                }
            }
            else
            {
                foreach (T obj in collection)
                {
                    Insert(index++, obj);
                }
            }

            ++version;
        }


        public int LastIndexOf(T item)
        {
            return size == 0 ? -1 : LastIndexOf(item, size - 1, size);
        }


        public int LastIndexOf(T item, int index) => LastIndexOf(item, index, index + 1);
        
        public int LastIndexOf(T item, int index, int count)
        {
            if (size == 0)
            {
                return -1;
            }
            
            return Array.LastIndexOf(items, item, index, count);
        }


        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }


        void IList.Remove(object item)
        {
            if (!IsCompatibleObject(item))
            {
                return;
            }

            Remove((T)item);
        }


        public int RemoveAll(Predicate<T> match)
        {
            int index1 = 0;
            while (index1 < size && !match(items[index1]))
                ++index1;
            if (index1 >= size)
            {
                return 0;
            }

            int index2 = index1 + 1;
            while (index2 < size)
            {
                while (index2 < size && match(items[index2]))
                    ++index2;
                if (index2 < size)
                {
                    items[index1++] = items[index2++];
                }
            }

            Array.Clear(items, index1, size - index1);
            int num = size - index1;
            size = index1;
            ++version;
            return num;
        }


        public void RemoveAt(int index)
        {
            --size;
            if (index < size)
            {
                Array.Copy(items, index + 1, items, index, size - index);
            }

            items[size] = default;
            ++version;
        }


        public void RemoveRange(int index, int count)
        {
            if (count <= 0)
            {
                return;
            }

            int size = this.size;
            this.size -= count;
            if (index < this.size)
            {
                Array.Copy(items, index + count, items, index, this.size - index);
            }

            Array.Clear(items, this.size, count);
            ++version;
        }


        public void Reverse() => Reverse(0, Count);


        public void Reverse(int index, int count)
        {
            Array.Reverse((Array)items, index, count);
            ++version;
        }


        public void Sort() => Sort(0, Count, null);


        public void Sort(IComparer<T> comparer) => Sort(0, Count, comparer);


        public void Sort(int index, int count, IComparer<T> comparer)
        {
            Array.Sort(items, index, count, comparer);
            ++version;
        }

        public sealed class FunctorComparer : IComparer<T>
        {
            private Comparison<T> comparison;

            public FunctorComparer(Comparison<T> comparison) => this.comparison = comparison;

            public int Compare(T x, T y) => comparison(x, y);
        }

        public void Sort(Comparison<T> comparison)
        {
            if (size <= 0)
            {
                return;
            }

            Array.Sort(items, 0, size, new FunctorComparer(comparison));
        }


        public T[] ToArray()
        {
            T[] destinationArray = new T[size];
            Array.Copy(items, 0, destinationArray, 0, size);
            return destinationArray;
        }


        public void TrimExcess()
        {
            if (size >= (int)(items.Length * 0.9))
            {
                return;
            }

            Capacity = size;
        }


        public bool TrueForAll(Predicate<T> match)
        {
            for (int index = 0; index < size; ++index)
            {
                if (!match(items[index]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static IList<T> Synchronized(LSList<T> list)
        {
            return new SynchronizedList(list);
        }

        [Serializable]
        internal class SynchronizedList : IList<T>
        {
            private LSList<T> _list;
            private object _root;

            internal SynchronizedList(LSList<T> list)
            {
                _list = list;
                _root = ((ICollection)list).SyncRoot;
            }

            public int Count
            {
                get
                {
                    lock (_root)
                        return _list.Count;
                }
            }

            public bool IsReadOnly => ((ICollection<T>)_list).IsReadOnly;

            public void Add(T item)
            {
                lock (_root)
                    _list.Add(item);
            }

            public void Clear()
            {
                lock (_root)
                    _list.Clear();
            }

            public bool Contains(T item)
            {
                lock (_root)
                    return _list.Contains(item);
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                lock (_root)
                    _list.CopyTo(array, arrayIndex);
            }

            public bool Remove(T item)
            {
                lock (_root)
                    return _list.Remove(item);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                lock (_root)
                    return _list.GetEnumerator();
            }

            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                lock (_root)
                    return ((IEnumerable<T>)_list).GetEnumerator();
            }

            public T this[int index]
            {
                get
                {
                    lock (_root)
                        return _list[index];
                }
                set
                {
                    lock (_root)
                        _list[index] = value;
                }
            }

            public int IndexOf(T item)
            {
                lock (_root)
                    return _list.IndexOf(item);
            }

            public void Insert(int index, T item)
            {
                lock (_root)
                    _list.Insert(index, item);
            }

            public void RemoveAt(int index)
            {
                lock (_root)
                    _list.RemoveAt(index);
            }
        }


        [Serializable]
        public struct Enumerator : IEnumerator<T>
        {
            private LSList<T> list;
            private int index;
            private int version;
            private T current;

            internal Enumerator(LSList<T> list)
            {
                this.list = list;
                index = 0;
                version = list.version;
                current = default;
            }


            public void Dispose()
            {
            }


            public bool MoveNext()
            {
                LSList<T> list = this.list;
                if (version != list.version || (uint)index >= (uint)list.size)
                {
                    return MoveNextRare();
                }

                current = list.items[index];
                ++index;
                return true;
            }

            private bool MoveNextRare()
            {
                index = list.size + 1;
                current = default;
                return false;
            }


            public T Current => current;


            object IEnumerator.Current => Current;


            void IEnumerator.Reset()
            {
                index = 0;
                current = default;
            }
        }
    }
}