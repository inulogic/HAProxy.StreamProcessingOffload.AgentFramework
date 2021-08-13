using HAProxy.StreamProcessingOffload.AgentFramework;
using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Agent
{
    public class SpoaApplication : ISpoaApplication
    {
        public Task<IEnumerable<SpopAction>> ProcessMessagesAsync(long streamId, IEnumerable<SpopMessage> messages)
        {
            var responseActions = new List<SpopAction>();

            foreach (var myMessage in messages)
            {
                if (myMessage.Name == "check-client-ip")
                {
                    int ip_score = 10;

                    if (IPAddress.IsLoopback((IPAddress)myMessage.Args["ip"])) ip_score = 20;

                    SpopAction setVar = new SetVarAction(VarScope.Session, "ip_score", ip_score);
                    responseActions.Add(setVar);
                }
            }

            return Task.FromResult((IEnumerable<SpopAction>)responseActions);
        }
    }
}
