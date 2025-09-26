using System;
using System.Collections;
using System.Collections.Generic;

namespace LSCore.DataStructs
{
    public readonly struct ArraySlice<T> : IEnumerable<T>
    {
        public readonly static ArraySlice<T> empty = Array.Empty<T>().Slice(..);
        public readonly T[] array;
        public readonly int start;
        public readonly int length;
    
        public ArraySlice(T[] array, int start, int length)
        {
            if (start < 0 || start > array.Length || length < 0 || start + length > array.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
    
            this.array = array;
            this.start = start;
            this.length = length;
        }
    
        public ref T this[int index]
        {
            get
            {
                if (index < 0 || index >= length) throw new ArgumentOutOfRangeException();
                
                return ref array[start + index];
            }
        }
    
        public int Length => length;
    
        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < length; i++)
            {
                yield return array[start + i];
            }
        }
    
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    
    public readonly struct ListSlice<T> : IEnumerable<T>
    {
        private readonly IList<T> list;
        private readonly int start;
        private readonly int count;
    
        public ListSlice(IList<T>  list, int start, int count)
        {
            if (start < 0 || start > list.Count || count < 0 || start + count > list.Count)
            {
                throw new ArgumentOutOfRangeException();
            }
    
            this.list = list;
            this.start = start;
            this.count = count;
        }
    
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException();
                return list[start + index];
            }
            set
            {
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException();
                list[start + index] = value;
            }
        }
    
        public int Count => count;
    
        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < count; i++)
            {
                yield return list[start + i];
            }
        }
    
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    
    public readonly struct Array2DSlice<T>
    {
        private readonly T[,] list;
        private readonly (int start, int count)[] ranges;
    
        public Array2DSlice(T[,] list, (int start, int count) x, (int start, int count) y)
        {
            ranges = new[] { x, y };
            
            for (var i = 0; i < ranges.Length; i++)
            {
                var r = ranges[i];
                var count = list.GetLength(i);
                
                if (r.start < 0 || r.start > count || r.count < 0 || r.start + r.count > count)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            
            this.list = list;
        }

        public int GetLength(int index) => ranges[index].count;
        
        public T this[int x, int y]
        {
            get
            {
                if (x < 0 || x >= ranges[0].count) throw new ArgumentOutOfRangeException();
                if (y < 0 || y >= ranges[1].count) throw new ArgumentOutOfRangeException();
                return list[ranges[0].start + x, ranges[1].start + y];
            }
            set
            {
                if (x < 0 || x >= ranges[0].count) throw new ArgumentOutOfRangeException();
                if (y < 0 || y >= ranges[1].count) throw new ArgumentOutOfRangeException();
                list[ranges[0].start + x, ranges[1].start + y] = value;
            }
        }
    }
    
    public static class IListExtensions
    {
        public static ArraySlice<T> Slice<T>(this T[] list, Range range)
        {
            var count = list.Length;
            var start = range.Start.GetOffset(count);
            var end = range.End.GetOffset(count);
            count = end - start;
    
            return new ArraySlice<T>(list, start, count);
        }
        
        public static ListSlice<T> Slice<T>(this IList<T> list, Range range)
        {
            var count = list.Count;
            var start = range.Start.GetOffset(count);
            var end = range.End.GetOffset(count);
            count = end - start;
    
            return new ListSlice<T>(list, start, count);
        }
        
        public static Array2DSlice<T> Slice<T>(this T[,] list, Range x, Range y)
        {
            Range[] ranges = {x, y};
            (int start, int count)[] iranges = new (int start, int count)[2];
            
            for (var i = 0; i < ranges.Length; i++)
            {
                var r = ranges[i];
                var count = list.GetLength(i);
                var start = r.Start.GetOffset(count);
                var end = r.End.GetOffset(count);
                count = end - start;
                
                iranges[i] = (start, count);
            }
            
            return new Array2DSlice<T>(list, iranges[0], iranges[1]);
        }
    }
}
