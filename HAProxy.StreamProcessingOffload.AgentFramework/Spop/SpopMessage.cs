namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System.Collections.Generic;

public class SpopMessage(string name)
{
    public string Name { get; internal set; } = name;

    public IDictionary<string, object> Args { get; internal set; } = new Dictionary<string, object>();
}
