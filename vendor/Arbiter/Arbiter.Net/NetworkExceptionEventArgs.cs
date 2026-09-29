namespace Arbiter.Net;

public class NetworkExceptionEventArgs : EventArgs
{
   public Exception Exception { get; }

   public NetworkExceptionEventArgs(Exception exception)
   {
      Exception = exception;
   }
}