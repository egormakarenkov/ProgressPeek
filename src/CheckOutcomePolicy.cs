using System;
using System.IO;
namespace ProgressPeek {
 public static class CheckOutcomePolicy {
  public static string Failure(Exception error){var cli=error as CliFailureException;if(cli!=null)return !cli.ToolUsed&&cli.Message=="Estimator connection failed; no automatic retry."?"transient":"permanent";return error is TimeoutException||error is IOException?"transient":"permanent";}
 }
}
