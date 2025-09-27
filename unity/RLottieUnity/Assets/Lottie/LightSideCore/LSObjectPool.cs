using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LSCore
{
    public class LSObjectPool<T> : IEnumerable<T>
    {
        internal readonly HashSet<T> releasedSet;
        public readonly HashSet<T> activeSet;
        protected Func<T> createFunc;
        public event Action<T> Created;
        public event Action<T> CreatedOrGot;
        public event Action<T> Got;
        public event Action<T> Released;
        public event Action<T> Removed;

        private bool shouldStoreActive;
        public int CountAll { get; private set; }
        public int CountActive => CountAll - CountInactive;
        public int CountInactive => releasedSet.Count;
        
        public LSObjectPool(
            Func<T> createFunc,
            int capacity = 10,
            bool shouldStoreActive = false)
        {
            releasedSet = new HashSet<T>(capacity);
            this.createFunc = createFunc;
            this.shouldStoreActive = shouldStoreActive; 
            if (shouldStoreActive)
            {
                activeSet = new HashSet<T>(capacity);
                CreatedOrGot += AddActive;
                Released += RemoveActive;
            }
        }

        private void AddActive(T obj) => activeSet.Add(obj);
        private  void RemoveActive(T obj) => activeSet.Remove(obj);
        
        public T Get()
        {
            T obj;
            if (releasedSet.Count == 0)
            {
                obj = createFunc();
                Created?.Invoke(obj);
                ++CountAll;
            }
            else
            {
                using var enumerator = releasedSet.GetEnumerator();
                enumerator.MoveNext();
                obj = enumerator.Current;
                releasedSet.Remove(obj);
                Got?.Invoke(obj);
            }
            
            CreatedOrGot?.Invoke(obj);
            return obj;
        }
        

        public void Release(T element)
        {
            releasedSet.Add(element);
            Released?.Invoke(element);
        }
        
        public void Release(IEnumerable<T> elements)
        {
            foreach (var element in elements)
            {
                Release(element);
            }
        }

        public void ReleaseAll()
        {
            if (activeSet != null)
            {
                Released -= RemoveActive;

                if (Released != null)
                {
                    foreach (var element in activeSet)
                    {
                        releasedSet.Add(element);
                        Released(element);
                    }
                }
                else
                {
                    foreach (var element in activeSet)
                    {
                        releasedSet.Add(element);
                    }
                }
                
                Released += RemoveActive;
                activeSet.Clear();
            }
            else
            {
                Debug.LogWarning($"{GetType().Name} shouldStoreActive is false, but you are trying to ReleaseAll. Set shouldStoreActive as true");
            }
        }

        
        public bool Remove(T element)
        {
            if (releasedSet.Remove(element))
            {
                Removed?.Invoke(element);
                activeSet?.Remove(element);
                CountAll--;
                return true;
            }

            return false;
        }
        
        public void Remove(IEnumerable<T> elements)
        {
            foreach (var element in elements)
            {
                Remove(element);
            }
        }

        public void Clear()
        {
            if (Removed != null)
            {
                foreach (T obj in releasedSet)
                {
                    Removed(obj);
                }

                if (activeSet != null)
                {
                    foreach (T obj in activeSet)
                    {
                        Removed(obj);
                    }
                }
            }
            
            activeSet?.Clear();
            releasedSet.Clear();
            CountAll = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var element in releasedSet)
            {
                yield return element;
            }

            if (activeSet != null)
            {
                foreach (var t in activeSet)
                {
                    yield return t;
                }
            }
        }
        
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}