using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using LlamAcademy.Dinos.Utility;
using Unity.AI.Navigation;

namespace LlamAcademy.Dinos.Unit
{
    [RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
    public class AgentLinkMover : MonoBehaviour
    {
        private NavMeshAgent Agent;
        private Animator Animator;

        private void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            Animator = GetComponent<Animator>();
        }

        private IEnumerator Start()
        {
            Agent.autoTraverseOffMeshLink = false;
            while (true)
            {
                if (!Agent.isOnOffMeshLink)
                {
                    yield return null;
                    continue;
                }

                OffMeshLinkData offMeshLinkData = Agent.currentOffMeshLinkData;
                NavMeshLink link = (NavMeshLink)Agent.navMeshOwner;

                if (Vector3.Distance(offMeshLinkData.endPos, Agent.destination) <
                    Vector3.Distance(offMeshLinkData.startPos, Agent.destination))
                {
                    yield return StartCoroutine(MoveAtNormalSpeed());
                }

                Agent.CompleteOffMeshLink();
                yield return null;
            }
        }

        private IEnumerator MoveAtNormalSpeed()
        {
            OffMeshLinkData data = Agent.currentOffMeshLinkData;
            Vector3 endPosition = data.endPos + Vector3.up * Agent.baseOffset;
            yield return StartCoroutine(MoveToTargetAtNormalSpeed(endPosition));
        }

        private IEnumerator MoveToTargetAtNormalSpeed(Vector3 target, float threshold = 0.01f)
        {
            while (Vector3.Distance(Agent.transform.position, target) > threshold)
            {
                Agent.transform.position = Vector3.MoveTowards(
                    Agent.transform.position,
                    target,
                    Agent.speed * Time.fixedDeltaTime);
                Animator.SetFloat(AnimationConstants.SPEED_PARAMETER, Agent.speed);
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
