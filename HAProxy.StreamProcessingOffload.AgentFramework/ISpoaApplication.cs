namespace HAProxy.StreamProcessingOffload.AgentFramework;
using System.Collections.Generic;
using System.Threading.Tasks;
using HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public interface ISpoaApplication
{
    Task<IEnumerable<SpopAction>> ProcessMessagesAsync(long streamId, IEnumerable<SpopMessage> messages);
}
