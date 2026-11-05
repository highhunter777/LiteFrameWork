using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface IPathProvider { string RootPath { get; } }   // Unity 侧：Application.persistentDataPath
}
