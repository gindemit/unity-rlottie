using System;
using UnityEngine;

namespace LSCore.Extensions
{
    public static class Extensions
    {
        public static void SafeInvoke(this Action action)
        {
            if (action != null)
            {
                foreach (var d in action.GetInvocationList())
                {
                    try
                    {
                        ((Action)d)();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
        }
        
        public static Rect SplitVertical(this Rect rect, int index, int count)
        {
            float height = (rect.height / count);
            rect.height = height;
            rect.y += height * index;
            return rect;
        }
        
        public static Rect Split(this Rect rect, int index, int count)
        {
            var totalWidth = (int)rect.width;
            var width = totalWidth / count;
            var remainder = totalWidth - width * count;
            var x = rect.x + width * index;

            if (index < remainder)
            {
                x += index;
                width += 1;
            }
            else
            {
                x += remainder;
            }

            rect.x = x;
            rect.width = width;
            return rect;
        }
        
        public static Rect TakeFromLeft(this ref Rect rect, float width)
        {
            var toTake = Math.Min(rect.width, width);
            var result = rect;
            result.width = toTake;
            rect.x += toTake;
            rect.width -= toTake;
            return result;
        }
        
        public static int ToInt(this bool b) => b ? 1 : 0;
        public static bool ToBool(this int b) => b != 0;
        public static float AspectRatio(this Texture texture) => (float)texture.width / texture.height;
    }
}