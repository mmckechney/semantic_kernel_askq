#nullable enable

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System.Text;
using syS = System;

namespace DocumentQuestions.Console
{

   public sealed class CustomConsoleFormatter : ConsoleFormatter
   {
      public CustomConsoleFormatter() : base("custom")
      {
      }

      public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
      {
         var (color, level) = LogLevelShort(logEntry.LogLevel);
         var stateText = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception)
            ?? logEntry.State?.ToString();

         var messages = stateText?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
         if (messages.Length == 0 && !string.IsNullOrEmpty(stateText))
         {
            messages = new[] { stateText };
         }

         if (logEntry.LogLevel != LogLevel.Information)
         {
            syS.Console.Write('[');
            syS.Console.ForegroundColor = color;
            syS.Console.Write(level);
            syS.Console.ResetColor();
            syS.Console.Write("] ");
         }

         foreach (var msg in messages)
         {
            var (messageColor, parsedMessage) = GetLogEntryColor(msg);
            syS.Console.ForegroundColor = messageColor;
            syS.Console.Write(parsedMessage);
            syS.Console.Write(' ');
            syS.Console.ResetColor();
         }

         syS.Console.WriteLine();
      }

      private static (syS.ConsoleColor, string) LogLevelShort(LogLevel level) => level switch
      {
         LogLevel.Trace => (syS.ConsoleColor.Blue, "TRC"),
         LogLevel.Debug => (syS.ConsoleColor.Blue, "DBG"),
         LogLevel.Information => (syS.ConsoleColor.White, "INF"),
         LogLevel.Warning => (syS.ConsoleColor.DarkYellow, "WRN"),
         LogLevel.Error => (syS.ConsoleColor.Red, "ERR"),
         LogLevel.Critical => (syS.ConsoleColor.DarkRed, "CRT"),
         _ => (syS.ConsoleColor.Cyan, "UNK"),
      };

      public (syS.ConsoleColor color, string message) GetLogEntryColor(string message)
      {
         var directiveIndex = message.IndexOf("**COLOR:", StringComparison.OrdinalIgnoreCase);
         if (directiveIndex < 0)
         {
            return (syS.ConsoleColor.White, message);
         }

         var colorSegment = message[(directiveIndex + "**COLOR:".Length)..];
         if (Enum.TryParse(colorSegment, ignoreCase: true, out syS.ConsoleColor parsed))
         {
            var payload = message[..directiveIndex];
            return (parsed, payload);
         }

         return (syS.ConsoleColor.White, message);
      }
   }

   public static class LoggerExtensions
   {
      public static void LogInformation(this ILogger logger, string message, syS.ConsoleColor color) => logger.LogInformation(FormatMessage(message, color));

      public static void LogDebug(this ILogger logger, string message, syS.ConsoleColor color) => logger.LogDebug(FormatMessage(message, color));

      public static void LogError(this ILogger logger, string message, syS.ConsoleColor color) => logger.LogError(FormatMessage(message, color));

      public static void LogWarning(this ILogger logger, string message, syS.ConsoleColor color) => logger.LogWarning(FormatMessage(message, color));

      public static void LogCritical(this ILogger logger, string message, syS.ConsoleColor color) => logger.LogCritical(FormatMessage(message, color));

      public static void LogTrace(this ILogger logger, string message, syS.ConsoleColor color) => logger.LogTrace(FormatMessage(message, color));

      public static void LogInformation(this ILogger logger, IReadOnlyDictionary<string, syS.ConsoleColor> messages) => logger.LogInformation(FormatMessages(messages));

      public static void LogDebug(this ILogger logger, IReadOnlyDictionary<string, syS.ConsoleColor> messages) => logger.LogDebug(FormatMessages(messages));

      public static void LogError(this ILogger logger, IReadOnlyDictionary<string, syS.ConsoleColor> messages) => logger.LogError(FormatMessages(messages));

      public static void LogWarning(this ILogger logger, IReadOnlyDictionary<string, syS.ConsoleColor> messages) => logger.LogWarning(FormatMessages(messages));

      public static void LogCritical(this ILogger logger, IReadOnlyDictionary<string, syS.ConsoleColor> messages) => logger.LogCritical(FormatMessages(messages));

      public static void LogTrace(this ILogger logger, IReadOnlyDictionary<string, syS.ConsoleColor> messages) => logger.LogTrace(FormatMessages(messages));

      private static string FormatMessages(IReadOnlyDictionary<string, syS.ConsoleColor> messages)
      {
         var builder = new StringBuilder();
         foreach (var message in messages)
         {
            builder.Append(message.Key);
            builder.Append(" **COLOR:");
            builder.Append(message.Value);
            builder.Append('|');
         }

         return builder.ToString();
      }

      private static string FormatMessage(string message, syS.ConsoleColor color) => $"{message} **COLOR:{color}";
   }
}