using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocumentQuestions.Library
{
   /// <summary>
   /// Extension methods for converting IEnumerable to IAsyncEnumerable
   /// </summary>
   public static class AsyncEnumerableExtensions
   {
      public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
      {
         foreach (var item in source)
         {
            yield return item;
            await Task.CompletedTask;
         }
      }
   }
}
