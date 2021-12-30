namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public class SetVarAction : SpopAction
{
    public SetVarAction(VarScope scope, string name, object value)
    {
        this.Scope = scope;
        this.Name = name;
        this.Value = value;
    }

    public VarScope Scope { get; private set; }
    public string Name { get; private set; }
    public object Value { get; private set; }
}
