// HomeCavecar.cs
using UnityEngine;
using DG.Tweening;

public class HomeCavecar : MonoBehaviour
{
    public static HomeCavecar homeCavecar;
    public bool canUse = true;
    public GameObject MapUI;
    public bool isPlayerEnter = false;
    
    [Header("UI动画设置")]
    [SerializeField] private float showAnimationDuration = 0.5f;
    [SerializeField] private float hideAnimationDuration = 0.3f;
    [SerializeField] private float showScaleMultiplier = 1.2f;
    
    private RectTransform mapUIRectTransform;
    private CanvasGroup mapUICanvasGroup;
    private Vector3 originalUIScale;
    private Tween currentUITween;
    private bool isUIActive = false;
    private bool isAnimating = false;

    [Header("交互反馈-波动")]
    [SerializeField] private float pulseDuration = 0.12f;
    [SerializeField] private float pulseScaleMultiplier = 1.12f;
    private Vector3 originalCarScale;
    private Tween carPulseTween;

    private void Start()
    {
        homeCavecar = this;
        originalCarScale = transform.localScale;
        
        InitializeMapUI();
    }

    private void Update()
    {
        if (isPlayerEnter && Input.GetKeyDown(KeyCode.E) && !isAnimating && canUse)
        {
            PlayPulse();
            AudioManager.Instance.PlayAudio("3");
            ToggleMapUI();

        }
        
        if (Input.GetKeyDown(KeyCode.Escape) && isUIActive && !isAnimating)
        {
            AudioManager.Instance.PlayAudio("3");
            HideMapUI();
            PlayerStateManager.instance.currentState=PlayerState.UpGround;
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && canUse)
        {
            GetComponent<InteractiveFeedback>()?.PlayFeedback();
            PlayPulse();
            isPlayerEnter = true;
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerEnter = false;
            HideMapUI();
            PlayerStateManager.instance.currentState=PlayerState.UpGround;
        }
    }
    
    private void InitializeMapUI()
    {
        if (MapUI == null) return;
        
        MapUI.SetActive(true);
        
        mapUIRectTransform = MapUI.GetComponent<RectTransform>();
        mapUICanvasGroup = MapUI.GetComponent<CanvasGroup>();
        if (mapUICanvasGroup == null) mapUICanvasGroup = MapUI.AddComponent<CanvasGroup>();
        
        originalUIScale = mapUIRectTransform.localScale;
        mapUIRectTransform.localScale = Vector3.zero;
        mapUICanvasGroup.alpha = 0f;
        MapUI.SetActive(false);
    }
    
    private void ToggleMapUI()
    {
        if (isUIActive)
        {
            HideMapUI();
        }
        else
        {
            ShowMapUI();
        }
    }
    
    private void ShowMapUI()
    {
        if (MapUI == null || isUIActive || isAnimating) return;
        
        isAnimating = true;
        isUIActive = true;
        MapUI.SetActive(true);
        // The old bottom navigation is optional; UpGround no longer contains it.
        var navigation = UIFloatingButtonGroup.Instance;
        if (navigation != null) navigation.DeactivateAllItems();
        if (currentUITween != null && currentUITween.IsActive())
        {
            currentUITween.Kill();
        }
        
        Vector3 overshootScale = originalUIScale * showScaleMultiplier;
        
        Sequence sequence = DOTween.Sequence();
        sequence.Append(mapUIRectTransform.DOScale(overshootScale, showAnimationDuration * 0.6f)
            .SetEase(Ease.OutBack));
        sequence.Join(mapUICanvasGroup.DOFade(1f, showAnimationDuration * 0.4f));
        sequence.Append(mapUIRectTransform.DOScale(originalUIScale, showAnimationDuration * 0.4f)
            .SetEase(Ease.OutBack));
        sequence.OnComplete(() => isAnimating = false);
        
        currentUITween = sequence;
        PlayerStateManager.instance.currentState=PlayerState.UI;
    }
    
    private void HideMapUI()
    {
        if (MapUI == null || !isUIActive || isAnimating) return;
        
        isAnimating = true;
        isUIActive = false;
        
        if (currentUITween != null && currentUITween.IsActive())
        {
            currentUITween.Kill();
        }
        
        Vector3 initialScale = mapUIRectTransform.localScale * 1.1f;
        
        Sequence sequence = DOTween.Sequence();
        sequence.Append(mapUIRectTransform.DOScale(initialScale, hideAnimationDuration * 0.2f)
            .SetEase(Ease.InBack));
        sequence.Join(mapUICanvasGroup.DOFade(0.8f, hideAnimationDuration * 0.2f));
        sequence.Append(mapUIRectTransform.DOScale(Vector3.zero, hideAnimationDuration * 0.8f)
            .SetEase(Ease.InBack));
        sequence.Join(mapUICanvasGroup.DOFade(0f, hideAnimationDuration * 0.6f));
        sequence.OnComplete(() => {
            MapUI.SetActive(false);
            isAnimating = false;
        });
        
        currentUITween = sequence;
        
    }
    
    public void CloseMapUI()
    {
        HideMapUI();
    }
    
    public void OpenMapUI()
    {
        ShowMapUI();
    }
    
    public bool IsMapUIActive()
    {
        return isUIActive;
    }

    private void OnDestroy()
    {
        if (currentUITween != null && currentUITween.IsActive())
        {
            currentUITween.Kill();
        }
        if (carPulseTween != null && carPulseTween.IsActive())
        {
            carPulseTween.Kill();
        }
    }

    private void PlayPulse()
    {
        if (!isActiveAndEnabled) return;
        if (carPulseTween != null && carPulseTween.IsActive())
        {
            carPulseTween.Kill();
        }

        transform.localScale = originalCarScale;
        Vector3 peak = originalCarScale * Mathf.Max(1f, pulseScaleMultiplier);
        float duration = Mathf.Max(0.01f, pulseDuration);

        carPulseTween = transform.DOScale(peak, duration * 0.5f)
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
            {
                carPulseTween = transform.DOScale(originalCarScale, duration * 0.5f).SetEase(Ease.InQuad);
            });
    }
}
