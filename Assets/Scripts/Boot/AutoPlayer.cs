using Game.Core;
using Game.Gameplay;
using Game.UI;
using UnityEngine;

namespace Game.Boot
{
    /// <summary>
    /// 自动试玩（开发用）：按 F9 让游戏自己打，不用手点就能看整局流程与结算面板。
    ///
    /// 走的是与玩家点击完全相同的路径（BoardController.TryRandomMove → 同样的计步、结算、事件），
    /// 所以能真实检验 HUD 刷新、结算面板弹出、再来一局这条链是否正常。
    ///
    /// 60 手用完必须**停住**，不能自己重开：
    ///   重开只能由玩家点「再来一局」触发。自动重开会绕过结算面板的遮挡与
    ///   BoardController.IsFinished 这道拦截，等于用演示脚本走了一条玩家走不到的路，
    ///   演示就不再能证明「结算后点不动」。
    ///
    /// **正式包里也有**：F9 自动演示、M 静音，两个键都是给玩家/试玩者用的，不做 UI，
    /// 说明写在新手指引里。不按 F9 时本组件什么都不做。
    ///
    /// 常用调法：把 Interval 调小（0.02）就是快进；调大（0.5）方便肉眼观察单步。
    /// </summary>
    public sealed class AutoPlayer : MonoBehaviour
    {
        [SerializeField] float _interval = 0.10f;
        [SerializeField] bool _startRunning = false;

        BoardController _controller;
        float _timer;

        public bool Running { get; private set; }

        void Start()
        {
            _controller = GetComponent<BoardController>();
            if (_controller == null) _controller = FindObjectOfType<BoardController>();

            Running = _startRunning && _controller != null;

            Debug.Log("[AutoPlayer] 按 F9 开始/暂停自动试玩，约 " + Mathf.RoundToInt(1f / _interval) + " 手/秒");
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9)) Toggle();
            if (Input.GetKeyDown(KeyCode.M)) AudioBank.ToggleMute();

            if (!Running || _controller == null) return;

            // 动画没播完就等 —— 否则 0.1 秒一手会与 0.4 秒的动画打架
            if (_controller.IsAnimating) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = _interval;

            // 结算即停：剩下的交给玩家点「再来一局」，自动演示不越权
            if (_controller.IsFinished)
            {
                ScoreKeeper result = _controller.Score;
                Stop("本局结算：" + result.Score + " 分，" + result.Stars
                    + " 星，最大连锁 ×" + _controller.MaxChainRound + "。点「再来一局」后可再按 F9");
                return;
            }

            _controller.TryRandomMove();
        }

        public void Toggle()
        {
            if (Running)
            {
                Stop("手动暂停");
                return;
            }

            if (_controller == null)
            {
                Debug.LogWarning("[AutoPlayer] 找不到 BoardController，无法开始");
                return;
            }

            if (_controller.IsFinished)
            {
                Debug.LogWarning("[AutoPlayer] 本局已结算，请先点「再来一局」再按 F9");
                return;
            }

            Running = true;
            _timer = 0f;
            Debug.Log("[AutoPlayer] 开始自动试玩");
        }

        /// <summary>外部（重新开始/退出）调用：强制停下自动演示。</summary>
        public void Stop(string reason)
        {
            if (!Running) return;
            Running = false;
            Debug.Log("[AutoPlayer] 已停止 —— " + reason);
        }
    }
}
