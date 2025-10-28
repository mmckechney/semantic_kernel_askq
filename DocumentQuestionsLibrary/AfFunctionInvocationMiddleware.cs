//using Microsoft.Extensions.Logging;
//using Microsoft.Agents.AI;
//using Microsoft.Extensions.AI;
//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;

//namespace DocumentQuestions.Library
//{
//   /// <summary>
//   /// Middleware for logging agent invocations (migrated from IFunctionInvocationFilter)
//   /// </summary>
//   public class AfFunctionInvocationMiddleware : DelegatingAIAgent
//   {
//      private readonly ILogger<AfFunctionInvocationMiddleware> log;

//      public AfFunctionInvocationMiddleware(AIAgent innerAgent, ILogger<AfFunctionInvocationMiddleware> log) 
//         : base(innerAgent)
//      {
//         this.log = log;
//      }

//      public AIAgent WrapAgent(AIAgent agent)
//      {
//         return new AfFunctionInvocationMiddleware(agent, log);
//      }

//      public override async Task<AgentRunResponse> RunAsync(
//         IEnumerable<ChatMessage> messages,
//         AgentThread? thread = null,
//         AgentRunOptions? options = null,
//         CancellationToken cancellationToken = default)
//      {
//         log.LogDebug("----------------");
//         log.LogDebug($"INVOKING AGENT: {Name ?? "Unknown"}{Environment.NewLine}MESSAGES:{Environment.NewLine}{string.Join(Environment.NewLine, messages.Select(m => $"{m.Role}: {m.Text}"))}");
         
//         var response = await base.RunAsync(messages, thread, options, cancellationToken);
         
//         log.LogDebug("----------------");
//         log.LogDebug($"INVOKED AGENT: {Name ?? "Unknown"}{Environment.NewLine}RESPONSE:{Environment.NewLine}{response.Value?.Text ?? "No response"}");
         
//         return response;
//      }

//      public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
//         IEnumerable<ChatMessage> messages,
//         AgentThread? thread = null,
//         AgentRunOptions? options = null,
//         CancellationToken cancellationToken = default)
//      {
//         log.LogDebug("----------------");
//         log.LogDebug($"INVOKING STREAMING AGENT: {Name ?? "Unknown"}{Environment.NewLine}MESSAGES:{Environment.NewLine}{string.Join(Environment.NewLine, messages.Select(m => $"{m.Role}: {m.Text}"))}");
         
//         await foreach (var update in base.RunStreamingAsync(messages, thread, options, cancellationToken))
//         {
//            // Log streaming chunks if needed
//            if (update.ResponseUpdate?.Kind == ChatMessageContentPartKind.Text)
//            {
//               log.LogTrace($"Chunk: {update.ResponseUpdate?.Text}");
//            }
//            yield return update;
//         }
         
//         log.LogDebug("----------------");
//         log.LogDebug($"COMPLETED STREAMING AGENT: {Name ?? "Unknown"}");
//      }
//   }
//}