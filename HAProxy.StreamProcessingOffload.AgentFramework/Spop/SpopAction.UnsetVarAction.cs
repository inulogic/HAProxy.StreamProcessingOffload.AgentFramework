namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    public class UnsetVarAction : SpopAction
    {
        public UnsetVarAction(VarScope scope, string name)
        {
            Scope = scope;
            Name = name;
        }

        public VarScope Scope { get; private set; }
        public string Name { get; private set; }
    }
}
