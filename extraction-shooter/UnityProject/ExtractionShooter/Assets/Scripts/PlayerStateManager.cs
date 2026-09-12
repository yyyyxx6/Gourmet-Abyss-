using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System;

public enum PlayerState
{
    UpGround,
    UI,
    Battle,
    Settlement
}

public class PlayerStateManager : MonoBehaviour
{
    public static PlayerStateManager instance;
    public PlayerState currentState;
    public bool isSettingUIActive = false;
    public GameObject SetttingPanel;
    // Start is called before the first frame update
    void Awake()
    {
        instance = this;
    }
    void Start()
    {
        DontDestroyOnLoad(this.gameObject);
        currentState = PlayerState.UpGround;
    }

    // Update is called once per frame
    void Update()
    {
        if (currentState == PlayerState.Settlement) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            AudioManager.Instance.PlayAudio("3");
            if (InterationManager.instance.skillTreeObject.activeInHierarchy)
            {
                InterationManager.instance.SwitchToHomeScene();
            }
            else
            {
                if (currentState == PlayerState.Battle || currentState == PlayerState.UpGround)
                {
                    if (HomeCavecar.homeCavecar.MapUI.activeInHierarchy)
                    {
                        return;
                    }
                    else
                    {
                        if (SetttingPanel.activeInHierarchy)
                        {
                            SetttingPanel.SetActive(false);
                            isSettingUIActive = false;
                        }
                        else
                        {
                            SetttingPanel.SetActive(true);
                            isSettingUIActive = true;
                        }
                    }

                }
            }

        }
    }
    public void ExitGame()
    {
        GameRoot.ResetAllAndLoad("MainUI");
    }
    public void CloseSettingUI()
    {
        SetttingPanel.SetActive(false);
        isSettingUIActive = false;
    }
    public void RestartGame()
    {
        ClearAllAndReloadScene();

    }

    public void ClearAllAndReloadScene(string sceneName = "UpGround")
    {
        GameRoot.ResetAllAndLoad(sceneName);
    }

    /// <summary>
    /// 先切到临时空场景再清场，用于需要更彻底隔离的情况。
    /// </summary>
    public void ForceReloadWithSceneRestart(string sceneName = "UpGround")
    {
        StartCoroutine(ReloadSceneWithCleanup(sceneName));
    }

    private System.Collections.IEnumerator ReloadSceneWithCleanup(string sceneName)
    {
        Scene tempScene = SceneManager.CreateScene("TempCleanupScene");
        SceneManager.SetActiveScene(tempScene);

        yield return new WaitForEndOfFrame();
        GameRoot.DestroyPersistentObjects();

        yield return new WaitForEndOfFrame();
        Resources.UnloadUnusedAssets();

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }
}
