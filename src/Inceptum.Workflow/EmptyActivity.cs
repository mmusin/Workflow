using System;

namespace Inceptum.Workflow
{
    public class EmptyActivity : ActivityBase<object,object>
    {
        public override ActivityResult Execute(object input, Action<object> processOutput)
        {
            return ActivityResult.Succeeded;
        }
    }
}