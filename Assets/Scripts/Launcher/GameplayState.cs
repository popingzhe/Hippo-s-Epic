using UnityEngine;

public class GameplayState : IGameState
{
    private readonly GameApp app;
    private UIManager _ui;
    private AudioManager _audio;
    private InputManager _inputManager;

    public GameplayState(GameApp app)
    {
        this.app = app;
        _ui = ManagerRegistry.Get<UIManager>();
        _audio = ManagerRegistry.Get<AudioManager>();
        _inputManager = ManagerRegistry.Get<InputManager>();
    }

    public void Enter()
    {
        Time.timeScale = 1f;

        _ui.Close<LoadingPanel>();
        _ui.Close<MainMenuPanel>();
        _ui.Open<MainPanelController>();

        _audio.PlayBGM("Gameplay");

        if (_inputManager != null)
            _inputManager.EnablePlayerAndUI();
    }

    public void Exit()
    {
        if (_bagOpen)
        {
            _bagOpen = false;
            Time.timeScale = 1f;
            _ui.Close<BagPanel>();
        }
        if (_taskOpen)
        {
            _taskOpen = false;
            Time.timeScale = 1f;
            _ui.Close<TaskPanel>();
        }
    }

    private bool _bagOpen;
    private bool _taskOpen;

    public void Update()
    {
        if (_inputManager == null) return;

        if (_bagOpen)
        {
            if (_inputManager.GameInput.UI.Cancel.WasPressedThisFrame())
            {
                if (_ui.HasPopupOpen())
                    _ui.CloseTopmost();
                else
                    CloseBag();
            }
            else if (_inputManager.GameInput.UI.Bag.WasPressedThisFrame())
            {
                CloseBag();
            }
            return;
        }

        if (_taskOpen)
        {
            if (_inputManager.GameInput.UI.Cancel.WasPressedThisFrame())
            {
                CloseTask();
            }
            else if (_inputManager.GameInput.UI.Task.WasPressedThisFrame())
            {
                CloseTask();
            }
            return;
        }

        if (_inputManager.GameInput.Player.Pause.WasPressedThisFrame())
        {
            app.TogglePause();
        }

        if (_inputManager.GameInput.UI.Bag.WasPressedThisFrame())
        {
            OpenBag();
        }
        else if (_inputManager.GameInput.UI.Task.WasPressedThisFrame())
        {
            OpenTask();
        }
        else if (_inputManager.GameInput.UI.Cancel.WasPressedThisFrame())
        {
            _ui.CloseTopmost();
        }
    }

    private void OpenBag()
    {
        _bagOpen = true;
        Time.timeScale = 0f;
        _inputManager.DisablePlayer();
        _inputManager.EnableUI();
        _ui.Open<BagPanel>();
    }

    private void CloseBag()
    {
        _bagOpen = false;
        Time.timeScale = 1f;
        _inputManager.EnablePlayerAndUI();
        _ui.Close<UseItemTip>();
        _ui.Close<BagPanel>();
    }

    public static void CloseBagIfOpen()
    {
        var app = GameApp.Instance;
        if (app != null && app.StateMachine.Current is GameplayState gs && gs._bagOpen)
            gs.CloseBag();
    }

    public void OpenBagPublic()
    {
        if (_taskOpen) CloseTask();     // 互斥：任务面板先关（其后 OpenBag 会重新设 timeScale/输入，幂等）
        if (!_bagOpen) OpenBag();
    }

    public void OpenTaskPublic()
    {
        if (_bagOpen) CloseBag();       // 互斥：背包先关
        if (!_taskOpen) OpenTask();
    }

    private void OpenTask()
    {
        _taskOpen = true;
        Time.timeScale = 0f;
        _inputManager.DisablePlayer();
        _inputManager.EnableUI();
        _ui.Open<TaskPanel>();
    }

    private void CloseTask()
    {
        _taskOpen = false;
        Time.timeScale = 1f;
        _inputManager.EnablePlayerAndUI();
        _ui.Close<TaskPanel>();
    }

    public static void CloseTaskIfOpen()
    {
        var app = GameApp.Instance;
        if (app != null && app.StateMachine.Current is GameplayState gs && gs._taskOpen)
            gs.CloseTask();
    }
}
