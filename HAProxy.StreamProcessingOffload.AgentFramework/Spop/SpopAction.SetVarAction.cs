namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public class SetVarAction(VarScope scope, string name, object value) : SpopAction
{
    public VarScope Scope { get; internal set; } = scope;
    public string Name { get; internal set; } = name;
    public object Value { get; internal set; } = value;
}
