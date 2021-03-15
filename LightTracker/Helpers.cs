using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LightTracker
{
    public static class Extensions
    {
        public static T Next<T>(this T value) where T : struct
        {
            if (!typeof(T).IsEnum)
                throw new ArgumentException($"Argument {typeof(T).FullName} is not an Enum");

            var enumValues = value.GetType().GetEnumValues();

            var nextIndex = Array.IndexOf(enumValues, value) + 1;
            if (nextIndex >= enumValues.Length)
                nextIndex = 0;

            return (T) enumValues.GetValue(nextIndex);
        }
    }
}

namespace LightTracker.Attributes
{
    // Attrib for props that require an update to underlying Unity light
    class LightPropertyAttribute : Attribute
    {
        public LightPropertyAttribute() { }
    }
}