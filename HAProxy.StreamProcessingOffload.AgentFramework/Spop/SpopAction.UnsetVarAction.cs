namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public class UnsetVarAction(VarScope scope, string name) : SpopAction
{
    public VarScope Scope { get; internal set; } = scope;
    public string Name { get; internal set; } = name;
}
