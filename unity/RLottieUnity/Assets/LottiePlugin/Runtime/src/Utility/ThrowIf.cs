using System;
using System.Collections;

namespace LottiePlugin.Utility
{
    public static class ThrowIf
    {
        public static class Argument
        {
            public static void IsNull(object argument, string argumentName)
            {
                if (argument == null)
                {
                    throw new ArgumentNullException(argumentName);
                }
            }
        }
        public static class String
        {
            public static void IsNullOrEmpty(string str, string strName)
            {
                if (str == null)
                {
                    throw new ArgumentNullException(strName);
                }
                if (str.Length == 0)
                {
                    throw new ArgumentException(strName + " is empty");
                }
            }
        }
        public static class Collection
        {
            public static void IsNullOrEmpty(ICollection collection, string collectionName)
            {
                if (collection == null)
                {
                    throw new ArgumentNullException(collectionName);
                }
                if (collection.Count == 0)
                {
                    throw new ArgumentException(collectionName + " is empty");
                }
            }
        }
        public static class Field
        {
            public static void IsNull(object argument, string argumentName)
            {
                if (argument == null)
                {
                    throw new InvalidOperationException(argumentName);
                }
            }
            public static void IsNotNull(object argument, string argumentName)
            {
                if (argument != null)
                {
                    throw new InvalidOperationException(argumentName);
                }
            }
        }
        public static class Value
        {
            public static void IsZero(int value, string valueName)
            {
                if (value == 0)
                {
                    throw new ArgumentException(valueName + " is 0");
                }
            }
            public static void IsZero(uint value, string valueName)
            {
                if (value == 0)
                {
                    throw new ArgumentException(valueName + " is 0");
                }
            }
            public static void IsZeroOrNegative(int value, string valueName)
            {
                if (value <= 0)
                {
                    throw new ArgumentException(valueName + " is equals or less than 0");
                }
            }
        }
        public static class Index
        {
            public static void IsOutOfBounds(int index, string collectionName, ICollection collection)
            {
                if (index < 0 || index >= collection.Count)
                {
                    throw new ArgumentOutOfRangeException("The index " + index + " is out of range in the " + collectionName + " collection");
                }
            }
        }
    }
}
