using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HAProxy.StreamProcessingOffload.AgentFramework
{
    public interface ISpoaApplication
    {
        Task<IEnumerable<SpopAction>> ProcessMessagesAsync(long streamId, IEnumerable<SpopMessage> messages);
    }
}
