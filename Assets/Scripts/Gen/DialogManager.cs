using System;
using System.Collections.Generic;
using System.Linq;
using cfg.cfg.dialog;
using cfg.cfg.dialogcontent;
using UnityEngine;
using System;

public enum DialogState
{
    Idle,
    Playing,
    Ended
}

public class DialogManager : MonoBehaviour
{
    public DialogState State { get; private set; } = DialogState.Idle;

    public event Action<int, int> OnDialogStart;
    public event Action<SpeakerSide, string, string, string> OnContent;

    /// <summary>已发放过 buff 的对话内容 id（同一条内容只发一次）</summary>
    private readonly HashSet<int> _appliedBuffContents = new HashSet<int>();

    /// <summary>对话内容携带 buff 时触发（参数=buff 表 id），由外部系统发放并弹提示。</summary>
    public event Action<int> OnDialogBuff;

    /// <summary>对话要求进入战斗时触发，参数=battle 表 id（type=3 的对话）。</summary>
    public event Action<int> OnDialogBattle;
    public event Action<List<OptionInfo>> OnOptions;
    public event Action OnDialogEnded;
    public event Action<int, string> OnEmotion;
    public event Action<int, int> OnSlotState;
    public event Action<cfg.cfg.dialogcontent.Dialogcontent> OnRightSlotUpdate;
    public event Action<string> OnBackgroundChange;
    public event Action<string> OnSpeakerAvatar;

    private cfg.Tables _tables;
    private Queue<Dialogcontent> _contentQueue;
    private Dialog _currentDialog;
    private bool _waitingCharacters;
    private int _transitionDepth;
    private bool _endingDialog;
    private int _rootDialogId;
    private const int MaxTransitionDepth = 50;

    private static readonly HashSet<int> _finishedDialogs = new HashSet<int>();
    public static bool IsDialogFinished(int dialogId) => _finishedDialogs.Contains(dialogId);
    public static IReadOnlyCollection<int> FinishedDialogs => _finishedDialogs;

    public static void RestoreFinishedDialogs(List<int> ids)
    {
        if (ids == null || ids.Count == 0) return;
        foreach (var id in ids)
            _finishedDialogs.Add(id);
        Debug.Log($"[DialogManager] 恢复已完成对话: {ids.Count} 条");
    }

    public int CurrentSpeakerId1 => _currentDialog?.Speakerid1 ?? 0;
    public int CurrentSpeakerId2 => _currentDialog?.Speakerid2 ?? 0;
    public cfg.cfg.dialog.Dialog CurrentDialog => _currentDialog;

    private void Awake()
    {
        ManagerRegistry.Register(this);
        TryAutoInit();
    }

    private void TryAutoInit()
    {
        if (_tables != null) return;
        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        if (tables != null)
            SetTables(tables);
    }

    protected void OnDestroy()
    {
        ManagerRegistry.Unregister<DialogManager>();
    }

    public void SetTables(cfg.Tables tables)
    {
        _tables = tables;
    }

    public void StartDialog(int dialogId)
    {
        if (_tables == null)
        {
            Debug.LogError("[DialogManager] Tables 未初始化");
            return;
        }

        var dialog = _tables.TbDialog.GetOrDefault(dialogId);
        if (dialog == null)
        {
            Debug.LogError($"[DialogManager] Dialog {dialogId} 不存在");
            return;
        }

        if (_endingDialog)
        {
            Debug.LogWarning($"[DialogManager] EndDialog 回调中不能启动新对话 {dialogId}，已延迟启动");
            return;
        }

        if (State == DialogState.Playing)
        {
            Debug.LogWarning($"[DialogManager] 对话正在进行中 (id={_currentDialog?.Id}), 先结束当前对话");
            EndDialog();
        }

        Debug.Log($"[DialogManager] 开始对话 {dialogId}, Type={dialog.Type}");
        _currentDialog = dialog;
        _rootDialogId = dialogId;
        State = DialogState.Playing;
        _waitingCharacters = false;
        _transitionDepth = 0;

        if (OnDialogStart != null)
        {
            _waitingCharacters = true;
            OnDialogStart.Invoke(dialog.Speakerid1, dialog.Speakerid2);
        }
        else
        {
            PlayCurrentDialog();
        }
    }

    public void NotifyCharactersReady()
    {
        if (!_waitingCharacters) return;
        if (_currentDialog == null) return;
        _waitingCharacters = false;
        PlayCurrentDialog();
    }

    public void ForceEndDialog()
    {
        if (State != DialogState.Playing) return;
        EndDialog();
    }

    public void Advance()
    {
        if (State != DialogState.Playing) return;
        if (_waitingCharacters) return;
        if (_currentDialog != null && _currentDialog.Type == 2) return;

        if (_contentQueue != null && _contentQueue.Count > 0)
        {
            PlayNextContent();
            return;
        }

        ProcessCurrentDialogFlow();
    }

    public void ChooseOption(int index)
    {
        if (State != DialogState.Playing || _currentDialog == null) return;
        if (_currentDialog.Type != 2) return;
        if (index < 0 || index >= _currentDialog.Param1.Count) return;

        int nextId = _currentDialog.Param1[index];
        TransitionToDialog(nextId);
    }

    private void TransitionToDialog(int dialogId)
    {
        _transitionDepth++;
        if (_transitionDepth > MaxTransitionDepth)
        {
            Debug.LogWarning($"[DialogManager] 对话跳转深度超过 {MaxTransitionDepth}，可能存在循环，强制结束");
            EndDialog();
            return;
        }

        var nextDialog = _tables.TbDialog.GetOrDefault(dialogId);
        if (nextDialog == null)
        {
            Debug.LogError($"[DialogManager] 目标 Dialog {dialogId} 不存在");
            EndDialog();
            return;
        }

        bool speakerChanged = _currentDialog == null
            || _currentDialog.Speakerid1 != nextDialog.Speakerid1
            || _currentDialog.Speakerid2 != nextDialog.Speakerid2;

        _currentDialog = nextDialog;

        if (speakerChanged && OnDialogStart != null)
        {
            _waitingCharacters = true;
            OnDialogStart.Invoke(nextDialog.Speakerid1, nextDialog.Speakerid2);
        }
        else
        {
            PlayCurrentDialog();
        }
    }

    private void PlayCurrentDialog()
    {
        // type=3：进入战斗（param2 或 param1[0] 为 battle 表 id）
        if (_currentDialog.Type == 3)
        {
            int battleId = 0;
            if (!int.TryParse((_currentDialog.Param2 ?? string.Empty).Trim(), out battleId) || battleId <= 0)
            {
                if (_currentDialog.Param1 != null && _currentDialog.Param1.Count > 0) battleId = _currentDialog.Param1[0];
            }

            Debug.Log($"[DialogManager] 对话 {_currentDialog.Id} 进入战斗 battleId={battleId}");
            EndDialog();
            OnDialogBattle?.Invoke(battleId);
            return;
        }

        if (_currentDialog.Type == 2)
        {
            ShowOptions();
            return;
        }

        _contentQueue = new Queue<Dialogcontent>(
            _tables.TbDialogcontent.DataList
                .Where(c => c.Dialogid == _currentDialog.Id)
                .OrderBy(c => c.Sortid)
        );

        if (_contentQueue.Count == 0)
        {
            ProcessCurrentDialogFlow();
            return;
        }

        PlayNextContent();
    }

    private void PlayNextContent()
    {
        if (_contentQueue == null || _contentQueue.Count == 0) return;

        var content = _contentQueue.Dequeue();
        var side = ResolveSide(content);
        string speakerName = ResolveSpeakerName(content, side);

        // 该条对话内容携带长期 buff：抛给外部处理（Gen 程序集不引用游戏逻辑，由 DialogManagerUI 接）
        if (content.Buffid > 0 && _appliedBuffContents.Add(content.Id))
        {
            Debug.Log($"[DialogManager] 对话内容 {content.Id} 携带 buff {content.Buffid}");
            OnDialogBuff?.Invoke(content.Buffid);
        }

        OnContent?.Invoke(side, content.Content, speakerName, content.GainItemText ?? "");

        var dialog = _currentDialog;
        if (dialog == null) return;

        int speakerId = side == SpeakerSide.Left ? dialog.Speakerid1
            : side == SpeakerSide.Right ? dialog.Speakerid2 : 0;
        if (speakerId != 0)
        {
            var person = _tables.TbPerson.GetOrDefault(speakerId);
            if (person != null && !string.IsNullOrEmpty(person.Avatar))
                OnSpeakerAvatar?.Invoke(person.Avatar);
        }

        if (!string.IsNullOrEmpty(content.Statename1) && dialog.Speakerid1 != 0)
            OnEmotion?.Invoke(dialog.Speakerid1, content.Statename1);

        if (!string.IsNullOrEmpty(content.Statename2) && dialog.Speakerid2 != 0)
            OnEmotion?.Invoke(dialog.Speakerid2, content.Statename2);

        if (content.Slotstateid1 != 0 && dialog.Speakerid1 != 0)
            OnSlotState?.Invoke(dialog.Speakerid1, content.Slotstateid1);
        if (content.Slotstateid2 != 0 && dialog.Speakerid2 != 0)
            OnSlotState?.Invoke(dialog.Speakerid2, content.Slotstateid2);

        OnRightSlotUpdate?.Invoke(content);

        if (!string.IsNullOrEmpty(content.Background))
            OnBackgroundChange?.Invoke(content.Background);
    }

    private void ProcessCurrentDialogFlow()
    {
        if (_currentDialog == null)
        {
            EndDialog();
            return;
        }

        if (_currentDialog.Param1 == null || _currentDialog.Param1.Count == 0)
        {
            EndDialog();
            return;
        }

        int nextId = _currentDialog.Param1[0];
        TransitionToDialog(nextId);
    }

    private void ShowOptions()
    {
        var options = new List<OptionInfo>();
        var param1 = _currentDialog.Param1;
        var condIds = _currentDialog.ConditionIds;

        for (int i = 0; i < param1.Count; i++)
        {
            int childId = param1[i];
            var child = _tables.TbDialog.GetOrDefault(childId);
            if (child != null)
            {
                int firstContentType = GetFirstContentType(childId);
                int condId = (condIds != null && i < condIds.Count) ? condIds[i] : 0;
                options.Add(new OptionInfo(child.SelectionName, childId, firstContentType, condId));
            }
        }

        OnOptions?.Invoke(options);
    }

    private int GetFirstContentType(int dialogId)
    {
        var first = _tables.TbDialogcontent.DataList
            .Where(c => c.Dialogid == dialogId)
            .OrderBy(c => c.Sortid)
            .FirstOrDefault();

        return first != null ? first.Type : 0;
    }

    private void EndDialog()
    {
        int finishedId = _currentDialog?.Id ?? 0;
        _endingDialog = true;
        State = DialogState.Ended;
        _waitingCharacters = false;
        _currentDialog = null;
        _contentQueue = null;
        if (finishedId > 0)
        {
            _finishedDialogs.Add(finishedId);
        }

        if (_rootDialogId > 0 && _rootDialogId != finishedId)
        {
            _finishedDialogs.Add(_rootDialogId);
        }

        OnDialogEnded?.Invoke();
        _endingDialog = false;
    }

    private SpeakerSide ResolveSide(Dialogcontent content)
    {
        switch (content.Type)
        {
            case 0: return SpeakerSide.Middle;
            case 1: return SpeakerSide.Left;
            case 2: return SpeakerSide.Right;
            default: return SpeakerSide.Middle;
        }
    }

    private string ResolveSpeakerName(Dialogcontent content, SpeakerSide side)
    {
        var dialog = _currentDialog;
        if (dialog == null) return "";

        switch (side)
        {
            case SpeakerSide.Left:
                return dialog.Speakerid1_Ref?.Name ?? "";
            case SpeakerSide.Right:
                return dialog.Speakerid2_Ref?.Name ?? "";
            default:
                return "";
        }
    }
}
