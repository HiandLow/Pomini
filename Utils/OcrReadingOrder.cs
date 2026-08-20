using System;
using System.Collections.Generic;
using System.Linq;

namespace PokemonHelper.Utils
{
    public static class OcrReadingOrder
    {
        public static IReadOnlyList<T> Sort<T>(IReadOnlyList<T> items, Func<T, (float CenterX, float CenterY, float Height)> geometry)
        {
            if (items.Count <= 1)
            {
                return items;
            }
            List<T> list = items.OrderBy((T i) => geometry(i).Item2).ToList();
            List<List<T>> list2 = new List<List<T>>();
            foreach (T item in list)
            {
                (float, float, float) tuple = geometry(item);
                if (list2.Count > 0)
                {
                    List<T> list3 = list2[list2.Count - 1];
                    if (Math.Abs(list3.Average((T x) => geometry(x).Item2) - tuple.Item2) < Math.Max(1f, tuple.Item3) * 0.6f)
                    {
                        list3.Add(item);
                        continue;
                    }
                }
                list2.Add(new List<T> { item });
            }
            return list2.SelectMany((List<T> l) => l.OrderBy((T x) => geometry(x).Item1)).ToList();
        }
    }
}
