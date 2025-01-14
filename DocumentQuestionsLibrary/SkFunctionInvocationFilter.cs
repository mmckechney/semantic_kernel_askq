using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentQuestions.Library
{
   public class SkFunctionInvocationFilter : IFunctionInvocationFilter
   {
      ILogger<SkFunctionInvocationFilter> log;
      public SkFunctionInvocationFilter(ILogger<SkFunctionInvocationFilter> log)
      {
         this.log = log;
      }
      public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
      {
         log.LogDebug("----------------");
         log.LogDebug($"INVOKING FUNCTION:{context.Function.Name}{Environment.NewLine}ARGUMENTS:{Environment.NewLine}{string.Join(Environment.NewLine, context.Arguments.Select(a => $"{a.Key}:{a.Value.ToString()}"))}");
         await next(context);
         log.LogDebug("----------------");
         log.LogDebug($"INVOKED FUNCTION:{context.Function.Name}{Environment.NewLine}ARGUMENTS:{Environment.NewLine}{string.Join(Environment.NewLine, context.Arguments.Select(a => $"{a.Key}:{a.Value.ToString()}"))}{Environment.NewLine}RESULT:{context.Result.ToString()}");

         var enumerable = context.Result.GetValue<IAsyncEnumerable<StreamingKernelContent>>();
         context.Result = new FunctionResult(context.Result, OverrideStreamingDataAsync(enumerable!));
      }

      private async IAsyncEnumerable<StreamingKernelContent> OverrideStreamingDataAsync(IAsyncEnumerable<StreamingKernelContent> data)
      {
         await foreach (var item in data)
         {
            //Override streaming data here
            yield return item;
         }
      }

   }
}

