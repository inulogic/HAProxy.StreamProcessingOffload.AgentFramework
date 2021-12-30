namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System.Collections.Generic;

public class SpopMessage
{
    public SpopMessage(string name)
    {
        this.Name = name;
        this.Args = new Dictionary<string, object>();
    }

    public string Name { get; private set; }

    public IDictionary<string, object> Args { get; private set; }
}
