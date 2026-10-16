using UnityEngine;

namespace ViveShuttleVR
{
    public sealed partial class BadmintonSession
    {
        private readonly SinglesMatchRules singles = new SinglesMatchRules();
        public SinglesMatchRules Singles { get { return singles; } }
        private bool standardTossPending, standardTossPlayerWon, standardInitialEndB;
        private bool standardServiceDelivered, standardServiceReceived;
        private Vector3 standardPlayerStanding;
        public bool StandardRallyActive { get { return IsStandardMatch && Running && Singles.Phase == SinglesPhase.Rally; } }
        public bool StandardServerIsPlayer { get { return Singles.Server == SinglesSide.Player; } }
        public bool StandardServicePending { get { return standardServiceDelivered && !standardServiceReceived; } }

        private PracticeMode NextMode()
        {
            return mode == PracticeMode.SinglesMatch ? PracticeMode.AiRally :
                mode == PracticeMode.AiRally ? PracticeMode.Training : PracticeMode.SinglesMatch;
        }
        private string ModeLabel()
        { return IsStandardMatch ? "正式单打" : IsAiMatch ? "AI 七分练习" : "喂球训练"; }

        private void ResetStandardMatch()
        {
            Singles.Reset(SinglesSide.Player);
            standardTossPending = IsStandardMatch;
            standardTossPlayerWon = Random.value < .5f;
            standardInitialEndB = false;
            standardServiceDelivered = standardServiceReceived = false;
            SetVenueEnd(false);
        }

        private void ShowStandardToss()
        {
            UpdateQuickGuide();
            statusText.text = "正式单打 / 21 分，三局两胜";
            detailText.text = standardTossPlayerWon ?
                "掷币：你先选  |  左扳机：先发  |  右扳机：先接\nAI 选择场端；小空间虚拟站位，不必实际走动" :
                "掷币：AI 选择先发\n你选场端：左扳机 A / 右扳机 B";
        }

        public void ResolveStandardTossChoice(bool leftTrigger)
        {
            if (!IsStandardMatch || !standardTossPending || !HeadReady()) return;
            SinglesSide first = standardTossPlayerWon && leftTrigger ? SinglesSide.Player : SinglesSide.Opponent;
            // If the human selects service/reception, the AI exercises its
            // remaining end choice. Otherwise the human selects the end.
            standardInitialEndB = standardTossPlayerWon ? Random.value < .5f : !leftTrigger;
            Singles.Reset(first);
            standardTossPending = false;
            UpdateQuickGuide();
            statusText.text = "掷币完成 / " + (first == SinglesSide.Player ? "你先发球" : "AI 先发球");
            detailText.text = "按右扳机确认站位并准备\n只在死球后调整虚拟球场，不移动真实头显";
            UpdateHud();
        }

        private void StartStandardPractice()
        {
            if (!HeadReady()) return;
            if (!calibrated) { Recenter(); return; }
            if (standardTossPending) { ResolveStandardTossChoice(false); return; }
            if (MatchComplete) { Recenter(); return; }
            if (Singles.Phase == SinglesPhase.GameInterval || Singles.Phase == SinglesPhase.MidGameInterval)
            {
                // The local AI is always willing to resume early. User input
                // supplies the human's agreement; the full timeout is available.
                Singles.CompleteInterval();
                Running = false;
            }
            if (Running) return;
            if (Singles.Phase == SinglesPhase.Ready)
            {
                if (!PrepareStandardServiceCourt()) return;
                if (!Singles.BeginRally()) return;
                standardServiceDelivered = standardServiceReceived = false;
                RallyStrokes = 0;
                LastNetClearance = float.NaN;
                landingMarker.gameObject.SetActive(false);
                feedTimer = 1.2f;
            }
            if (Singles.Phase != SinglesPhase.Rally) return;
            Running = true;
            statusText.text = (StandardServerIsPlayer ? "轮到你发球" : "准备接 AI 发球") +
                " / " + (Singles.ServerRight ? "右发球区" : "左发球区");
            detailText.text = standardServiceDelivered ? "已继续本回合 / 站位不变" : StandardServerIsPlayer ?
                "左扳机持球，松开后低手击球\n整个球低于 1.15 米，向上发向对角区" :
                "已确认接发准备 / AI 即将低手发球\n保持原位，留意浅绿色接球环";
            UpdateHud();
        }

        private bool PrepareStandardServiceCourt()
        {
            if (shuttle.Flying || HandServeHeld) return false;
            Vector3 eye; Quaternion heading;
            if (!TryGetHeadWorldPose(out eye, out heading)) return false;
            // Canonical player-facing coordinates keep the player looking
            // forward at both ends. Rotate only the venue art to represent
            // the end exchange; simulation/score keep explicit end ownership.
            standardPlayerStanding = new Vector3(Singles.ServerRight ? 1.295f : -1.295f, 0f, 0f);
            float floor = court.position.y;
            court.position = new Vector3(eye.x, floor, eye.z) - court.right * standardPlayerStanding.x;
            SetVenueEnd(Singles.EndsSwapped ^ standardInitialEndB);
            ConfigureDrillLayout();
            opponent.PositionForService(Singles.ServerRight);
            side = 1;
            PositionReachMarker();
            if (StandardServerIsPlayer)
                reachMarker.localPosition = standardPlayerStanding + new Vector3(.14f, 1.02f, .44f);
            targetMarker.gameObject.SetActive(false);
            Debug.Log("[Singles] Service placement; server=" + Singles.Server + "; right=" + Singles.ServerRight +
                "; virtualPlayer=" + standardPlayerStanding + "; swapped=" + Singles.EndsSwapped);
            return true;
        }

        private void SetVenueEnd(bool swapped)
        {
            if (court == null) return;
            Transform hall = court.Find("Regulation court and indoor hall");
            if (hall == null) return;
            hall.localPosition = swapped ? new Vector3(0f, 0f, 2f * BadmintonCourt.NetZ) : Vector3.zero;
            hall.localRotation = swapped ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        }

        private bool AdvanceStandardInterval(float dt)
        {
            if (Singles.Phase != SinglesPhase.MidGameInterval && Singles.Phase != SinglesPhase.GameInterval) return false;
            bool gameBreak = Singles.Phase == SinglesPhase.GameInterval;
            if (Singles.AdvanceInterval(dt))
            {
                Running = false;
                statusText.text = "间歇结束";
                detailText.text = "右扳机：确认站位，准备下一球";
                UpdateHud();
                return true;
            }
            statusText.text = (gameBreak ? "局间休息 " : "11 分间歇 ") + Mathf.CeilToInt(Singles.IntervalRemaining) + " 秒";
            bool endChange = gameBreak || (Singles.PlayerGames == 1 && Singles.OpponentGames == 1);
            detailText.text = "右扳机：提前继续  |  菜单键：暂停\n" +
                (endChange ? "换边在下一次确认准备时完成" : "确认准备后继续本局");
            return true;
        }

        private void ServeStandardOpponent()
        {
            if (!StandardRallyActive || standardServiceDelivered || StandardServerIsPlayer || shuttle.Flying) return;
            ShuttleState initial = default(ShuttleState); RacketContact contact = default(RacketContact);
            bool began = opponent.RegulationServePlanning ||
                opponent.BeginRegulationServePlanning(Singles.ServerRight, GetOpponentTarget(0));
            OpponentServePlanStatus planning = began ? opponent.AdvanceRegulationServePlanning(out initial, out contact) :
                OpponentServePlanStatus.Failed;
            if (planning == OpponentServePlanStatus.Pending)
            {
                statusText.text = "AI 准备发球";
                detailText.text = "保持原位，准备接发\n短按菜单键：暂停";
                return;
            }
            if (planning == OpponentServePlanStatus.Success && shuttle.LaunchStandardAiServe(initial, contact))
            {
                Feeds++;
                LastFeedProfile = new FeedProfile { sourceLocal = court.InverseTransformPoint(initial.position),
                    targetLocal = court.InverseTransformPoint(GetOpponentTarget(0)), label = "REGULATION AI SERVICE" };
                statusText.text = "AI 合规低手发球 / " + (Singles.ServerRight ? "右区" : "左区");
                Play(feedSound, .12f);
            }
            else if (Singles.Phase == SinglesPhase.Rally)
            {
                // A solver failure is an implementation limitation, not a
                // point awarded against either competitor.
                Singles.Let(Singles.CurrentRallyId);
                shuttle.Stop(); Running = false;
                statusText.text = "发球规划未完成 / 本球重发";
                detailText.text = "比分和发球权保留，按右扳机重试";
            }
        }

        // Highest point of the current displayed cork/feathers, in court units.
        // The full shuttle (not just the impact origin) must be below 1.15 m.
        public float StandardShuttleTop(ShuttleState state)
        {
            Vector3 axis = court.InverseTransformDirection(state.orientation * Vector3.up).normalized;
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - axis.y * axis.y));
            float cork = .0185f;
            float featherTop = axis.y * .08f + radial * .035f + .006f;
            float featherBase = axis.y * .018f + radial * .0105f + .006f;
            return court.InverseTransformPoint(state.position).y + Mathf.Max(cork, featherTop, featherBase);
        }

        public bool ValidateStandardContact(bool player, ShuttleState before, RacketContact contact, ImpactResult impact, out string fault)
        {
            fault = "";
            if (!StandardRallyActive) { fault = "NOT_READY"; return false; }
            Vector3 p = court.InverseTransformPoint(before.position);
            if (player ? p.z >= BadmintonCourt.NetZ : p.z <= BadmintonCourt.NetZ)
            { fault = "OVER_NET"; return false; }
            if (!standardServiceDelivered)
            {
                if (player != StandardServerIsPlayer) { fault = "WRONG_SERVER"; return false; }
                bool correctX = player ? Singles.ServerRight : !Singles.ServerRight;
                Vector3 virtualFeet = player ? standardPlayerStanding : opponent.FeetLocal;
                if (!BadmintonCourt.IsInServiceCourt(virtualFeet, !player, correctX, false))
                { fault = "SERVICE_SIDE"; return false; }
                if (StandardShuttleTop(before) >= 1.15f)
                { fault = "SERVICE_HEIGHT"; return false; }
                Vector3 velocity = court.InverseTransformDirection(impact.outgoingVelocity);
                if (velocity.y <= 0f || (player ? velocity.z <= 0f : velocity.z >= 0f))
                { fault = "SERVICE_UPWARD"; return false; }
            }
            return true;
        }

        public void OnStandardContact(bool player, ShuttleState before, RacketContact contact, ImpactResult impact)
        {
            if (!standardServiceDelivered) standardServiceDelivered = true;
            else if (!standardServiceReceived && player != StandardServerIsPlayer) standardServiceReceived = true;
        }

        public bool IsStandardServiceLandingLegal(Vector3 localPosition)
        {
            return BadmintonCourt.IsInServiceCourt(localPosition, StandardServerIsPlayer,
                StandardServerIsPlayer ? !Singles.ServerRight : Singles.ServerRight, false);
        }

        public bool OnStandardNetCrossing(Vector3 localPoint, int rallyId)
        {
            if (rallyId != Singles.CurrentRallyId || Singles.Phase != SinglesPhase.Rally) return false;
            if (Mathf.Abs(localPoint.x) <= BadmintonCourt.DoublesHalfWidth) return true;
            FinishStandardRally("NET", localPoint, shuttle.StandardPlayerLastHit, rallyId);
            return false;
        }

        public void FinishStandardRally(string reason, Vector3 landing, bool playerLastHit)
        { FinishStandardRally(reason, landing, playerLastHit, Singles.CurrentRallyId); }

        public void FinishStandardRally(string reason, Vector3 landing, bool playerLastHit, int rallyId)
        {
            if (!IsStandardMatch || Singles.Phase != SinglesPhase.Rally || rallyId != Singles.CurrentRallyId) return;
            bool explicitFault = reason.StartsWith("SERVICE_") || reason == "WRONG_SERVER" ||
                reason == "OVER_NET" || reason == "NET_TOUCH" || reason == "DOUBLE_HIT" || reason == "CONTACT_FAULT";
            bool playerWon = explicitFault ? !playerLastHit : !standardServiceDelivered ? !StandardServerIsPlayer :
                StandardServicePending ? (IsStandardServiceLandingLegal(landing) && reason != "NET" && reason != "OUT" ?
                    StandardServerIsPlayer : !StandardServerIsPlayer) : PlayerWinsAiPoint(reason, landing, playerLastHit);
            if (!Singles.AwardRally(playerWon ? SinglesSide.Player : SinglesSide.Opponent, rallyId)) return;
            shuttle.Stop(); heldServeHand = null; heldReleaseArmed = hasHeldSample = false;
            if (opponent != null) opponent.OnPointEnded();
            Score = PlayerPoints;
            landingMarker.localPosition = new Vector3(landing.x, .035f, landing.z);
            landingMarker.gameObject.SetActive(true);
            Running = Singles.Phase == SinglesPhase.MidGameInterval || Singles.Phase == SinglesPhase.GameInterval;
            statusText.text = MatchComplete ? (Singles.PlayerGames > Singles.OpponentGames ? "比赛结束 / 你获胜" : "比赛结束 / AI 获胜") :
                (playerWon ? "你得分" : "AI 得分") + " / " + StandardReason(reason);
            bool nextRight = Singles.Phase == SinglesPhase.GameInterval || Singles.ServerRight;
            detailText.text = MatchComplete ? "三局两胜结束 / 右扳机：新比赛" :
                "下一球：" + (StandardServerIsPlayer ? "你发球" : "AI 发球") + "，" + (nextRight ? "右区" : "左区") +
                "\n右扳机：确认下一球 / 休息中可提前继续";
            UpdateHud();
            Debug.Log("[Singles] Point; winner=" + (playerWon ? "Player" : "Opponent") + "; reason=" + reason +
                "; score=" + PlayerPoints + ":" + OpponentPoints + "; games=" + Singles.PlayerGames + ":" + Singles.OpponentGames +
                "; phase=" + Singles.Phase + "; nextServer=" + Singles.Server);
        }

        private static string StandardReason(string reason)
        {
            switch (reason)
            {
                case "SERVICE_HEIGHT": return "发球过高，整个球须低于 1.15 米";
                case "SERVICE_UPWARD": return "发球须向前上方飞行";
                case "SERVICE_SIDE": return "发球区错误";
                case "SERVICE_OUT": return "发球未落入对角区";
                case "WRONG_SERVER": return "发球方错误";
                case "OVER_NET": return "首触球越过球网";
                case "NET_TOUCH": return "球拍触网";
                case "DOUBLE_HIT": return "连续两次击球";
                case "NET": return "下网或未从网顶通过";
                case "OUT": return "出界";
                default: return "本球结束";
            }
        }
    }
}
