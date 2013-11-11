using System;
using System.Linq;

namespace Inceptum.Workflow
{
    public class ActivityState
    {
        public dynamic Values { get; set; }
        public string NodeName { get; set; }
        public ActivityResult Status { get; set; }
    }

    class NullExecutionObserver : IExecutionObserver
    {
        public void ActivityStarted(string node, string activityType, object inputValues)
        {
            
        }

        public void ActivityFinished(string node, string activityType, object outputValues)
        {
        }

        public void ActivityFailed(string node, string activityType, object outputValues)
        {
        }

        public void ActivityCorrupted(string node, string activityType)
        {
        }
    }

    internal class WorkflowExecutor<TContext> : IWorkflowVisitor<TContext, WorkflowState>
    {
        private readonly IActivityFactory m_Factory;
        private readonly INodesResolver<TContext> m_Nodes;
        private readonly Execution<TContext> m_Execution;
        private readonly TContext m_Context;
        private readonly IActivityExecutor m_ActivityExecutor;
        private bool m_Resuming = false;
        private readonly object m_Closure;
        private readonly IExecutionObserver m_ExecutionObserver;

        public WorkflowExecutor(Execution<TContext> execution, TContext context, INodesResolver<TContext> nodes, IActivityFactory factory,IActivityExecutor  activityExecutor,IExecutionObserver observer,object closure)
            :this(execution,context,nodes,factory,activityExecutor,observer)
        {
            m_Resuming = true;
            m_Closure = closure;
        }

        public WorkflowExecutor(Execution<TContext> execution, TContext context, INodesResolver<TContext> nodes, IActivityFactory factory, IActivityExecutor activityExecutor, IExecutionObserver observer)
        {
            m_ExecutionObserver = observer??new NullExecutionObserver();
            m_Context = context;
            m_Factory = factory;
            m_Execution = execution;
            m_Nodes = nodes;
            m_ActivityExecutor=activityExecutor;
        }




        public WorkflowState Visit<TActivity, TInput, TOutput>(GraphNode<TContext, TActivity, TInput, TOutput> node) where TActivity : IActivity<TInput, TOutput>
        {
            TActivity activity;
            if (typeof (TActivity) == typeof (GenericActivity))
                activity = (TActivity)(object)new GenericActivity(m_ActivityExecutor, node.ActivityType, node.Name);
            else
                activity = m_Factory.Create<TActivity, TInput, TOutput>();

            object activityOutput = null;
            ActivityResult result;
            m_Execution.ActiveNode = node.Name;
            if (m_Resuming)
            {
                result = activity.Resume(output =>
                {
                    activityOutput = output;
                    node.ProcessOutput(m_Context, output);
                }, m_Closure);
             }
            else
            {
                var activityInput = node.GetActivityInput(m_Context);

                m_ExecutionObserver.ActivityStarted(node.Name, node.ActivityType, activityInput);
                result = activity.Execute(activityInput, output =>
                    {
                        activityOutput = output;
                        node.ProcessOutput(m_Context, output);
                    });
            }

            m_Resuming = false;

            if (result == ActivityResult.Pending)
            {
                m_Execution.State = WorkflowState.InProgress;
                return WorkflowState.InProgress;
            }

            if (result == ActivityResult.None)
            {
                m_Execution.State = WorkflowState.Corrupted;
                m_ExecutionObserver.ActivityCorrupted(node.Name, node.ActivityType);
                return WorkflowState.Corrupted;
            }

            if (result == ActivityResult.Failed)
            {
                m_ExecutionObserver.ActivityFailed(node.Name, node.ActivityType, activityOutput);
            }

            if (result == ActivityResult.Succeeded)
            {
                m_ExecutionObserver.ActivityFinished(node.Name, node.ActivityType,activityOutput);
            }

            var next = node.Edges.SingleOrDefault(e => e.Condition(m_Context, result));

            if (next != null)
            {
                var nextNode = m_Nodes[next.Node];
                var nextResult = nextNode.Accept(this);
                return nextResult;
            }

            //TODO: =="end" is not good idea
            if (node.Name == "end" && result == ActivityResult.Succeeded)
            {
                m_Execution.State = WorkflowState.Complete;
                return WorkflowState.Complete;
            }

           //TODO: =="end" is not good idea
            if (node.Name == "fail")
            {
                m_Execution.State = WorkflowState.Failed;
                return WorkflowState.Failed;
            }

            m_Execution.State = WorkflowState.Corrupted;
            return WorkflowState.Corrupted;

        }
    }
}