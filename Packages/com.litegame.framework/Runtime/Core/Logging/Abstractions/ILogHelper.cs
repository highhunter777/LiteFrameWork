using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public enum LogLevel 
    {
        Info, Warning, Error, Fatal
    }

    public interface ILogHelper 
    {
        void Log(LogLevel level, string message); 
    }
}
