using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Util
{
    public static class UnsafeListExt
    {
        public static unsafe void CopyFrom<T>(this ref UnsafeList<T> ul, in UnsafeList<T> other) where T : unmanaged
        {
            ul.Resize(other.Length, NativeArrayOptions.ClearMemory);
            UnsafeUtility.MemCpy(ul.Ptr, other.Ptr, other.Length * UnsafeUtility.SizeOf<T>());
        }
        
        public static unsafe void CopyFrom<T>(this ref UnsafeList<T> ul, in NativeList<T> other) where T : unmanaged
        {
            ul.Resize(other.Length, NativeArrayOptions.ClearMemory);
            UnsafeUtility.MemCpy(ul.Ptr, other.GetUnsafePtr(), other.Length * UnsafeUtility.SizeOf<T>());
        }
        
        public static unsafe void CopyFrom<T>(this ref UnsafeList<T> ul, in NativeArray<T> other) where T : unmanaged
        {
            ul.Resize(other.Length, NativeArrayOptions.ClearMemory);
            UnsafeUtility.MemCpy(ul.Ptr, other.GetUnsafePtr(), other.Length * UnsafeUtility.SizeOf<T>());
        }

        public static unsafe void CopyFrom<T>(this ref NativeList<T> ul, in UnsafeList<T> other) where T : unmanaged
        {
            ul.Resize(other.Length, NativeArrayOptions.ClearMemory);
            UnsafeUtility.MemCpy(ul.GetUnsafePtr(), other.Ptr, other.Length * UnsafeUtility.SizeOf<T>());
        }

        
        public static void AddGrowth<T>(this ref UnsafeList<T> ul, in T element, float growth = 1.5f) where T : unmanaged
        {
            if (ul.Length == ul.Capacity)
            {
                var newCapacity = (int) Math.Max(ul.Capacity + 1, ul.Capacity * growth);
                ul.SetCapacity(newCapacity);
            }
            ul.Add(element);
        }
        
        public static void AddGrowth<T>(this ref NativeList<T> ul, in T element, float growth = 1.5f) where T : unmanaged
        {
            if (ul.Length == ul.Capacity)
            {
                var newCapacity = (int) Math.Max(ul.Capacity + 1, ul.Capacity * growth);
                ul.Capacity = newCapacity;
            }
            ul.Add(element);
        }
        
        public static IEnumerable<T> AsEnumerable<T>(this UnsafeList<T> ul) where T : unmanaged
        {
            for (var i = 0; i < ul.Length; i++)
            {
                yield return ul[i];
            }
        }
    }
}