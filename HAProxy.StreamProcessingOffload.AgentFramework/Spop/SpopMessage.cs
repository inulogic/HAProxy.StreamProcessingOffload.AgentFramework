using System.Collections.Generic;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    public class SpopMessage
    {
        public SpopMessage(string name)
        {
            Name = name;
            Args = new Dictionary<string, object>();
        }

        public string Name { get; private set; }

        public IDictionary<string, object> Args { get; private set; }
    }
}