using System;
using UnityEngine;


    [AddComponentMenu("Level 2D/Player Character")]
    public class PlayerCharacter : LevelObject, IFocusTarget
    {
        public new Rigidbody2D rigidbody2D;
        public float MoveSpeed = 2.5f;

        [Header("Flip")]
        [SerializeField] private bool faceRightByDefault = true;

        [Tooltip("翻转目标：只翻视觉层（默认自动取子节点 Visual）。根节点保持不翻，避免把挂在根下的 UI（Gain_exp 经验飘字等）一起镜像")]
        [SerializeField] private Transform visualRoot;

        [Header("动画")]
        [SerializeField] private AnimationController animationController;

        private Interactable _currentInteractable;
        private Vector3 _originalScale;
        private PlayerStateMachine _stateMachine;

        public Interactable CurrentInteractable => _currentInteractable;
        public PlayerStateMachine StateMachine => _stateMachine;
        public PlayerStateType CurrentStateType => _stateMachine.CurrentStateType;

        public void SetCurrentInteractable(Interactable interactable)
        {
            _currentInteractable = interactable;
        }

        public void ClearCurrentInteractable()
        {
            _currentInteractable = null;
        }

        private void Awake()
        {
            if (visualRoot == null)
            {
                visualRoot = transform.Find("Visual");
            }

            if (visualRoot == null)
            {
                Debug.LogWarning("[PlayerCharacter] 未找到 Visual 子节点，翻转回退到根节点（会连带镜像其下 UI）");
                visualRoot = transform;
            }

            _originalScale = visualRoot.localScale;

            if (animationController == null)
            {
                animationController = GetComponent<AnimationController>();
            }

            _stateMachine = new PlayerStateMachine(this, animationController);
        }

        private void Start()
        {
            _stateMachine.Initialize();
        }

        public void SetMoveInput(Vector2 vector2)
        {
            rigidbody2D.velocity = vector2 * MoveSpeed;

            if (vector2.x > 0.01f)
            {
                SetFacing(true);
            }
            else if (vector2.x < -0.01f)
            {
                SetFacing(false);
            }

            _stateMachine.Update(vector2);
        }

        private void SetFacing(bool faceRight)
        {
            Vector3 scale = _originalScale;

            if (faceRightByDefault)
            {
                scale.x = faceRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            }
            else
            {
                scale.x = faceRight ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
            }

            // 只翻视觉层：根节点不翻，挂在其下的 UI（Gain_exp 等）不会被镜像
            if (visualRoot != null)
            {
                visualRoot.localScale = scale;
            }
        }

        private void Update()
        {
            Vector3 position = transform.position;
            Vector2 position2D = new Vector2(position.x, position.y);
            transform.position = new Vector3(position2D.x, position2D.y, position2D.y);
            Moved?.Invoke(position2D);
        }

        public void PlayerExecute(int buttonIndex = 0)
        {
            if (_currentInteractable != null)
            {
                _currentInteractable.OnPlayerExecute(buttonIndex);
            }
        }

        public event Action<Vector2> Moved;
    }
