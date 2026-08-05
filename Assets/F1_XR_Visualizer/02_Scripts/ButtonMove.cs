using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class ButtonMove : MonoBehaviour
{
    [SerializeField] private float pressDistance = 0.01f;

    Vector3 currentPosition;
    XRSimpleInteractable interactable;

    private void Awake() // 게임 시작: 처음 위치 저장
    {
        currentPosition = transform.localPosition;
        interactable = GetComponent<XRSimpleInteractable>();
    }


    private void OnEnable()
    {
        interactable.selectEntered.AddListener(OnPress);
        interactable.selectExited.AddListener(OnRelease);
    }

    private void OnDisable()
    {
        interactable.selectEntered.RemoveListener(OnPress);
        interactable.selectExited.RemoveListener(OnRelease);
    }
    
    private void OnPress(SelectEnterEventArgs _)
    {
        transform.localPosition = currentPosition + new Vector3(0f, 0f, pressDistance);
    }

    private void OnRelease(SelectExitEventArgs _)
    {
        transform.localPosition = currentPosition;
    }
}
