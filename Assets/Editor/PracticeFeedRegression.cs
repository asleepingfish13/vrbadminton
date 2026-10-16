using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace ViveShuttleVR.Editor
{
    public static class PracticeFeedRegression
    {
        private static int checks;

        private struct Trajectory
        {
            public ShuttleState arrival;
            public Vector3 launch;
            public float apex, netHeight, angle;
            public Vector3 netCrossing;
            public bool crossedNet, hitNet;
        }

        public static int Run(BadmintonSession session)
        {
            checks = 0;
            TestSequences();
            TestProfiles();
            TestFocusedDrills();
            TestDistinctFlight();
            TestGroundDepths();
            TestMixedSession(session);
            TestCompactProfiles();
            TestCompactSession(session);
            Debug.Log("[Feed Test] PASS " + checks + " checks: balanced seeded order, preserved normal profiles, compact seated mixed/fixed targets and live physical contacts, real-height and 175 cm virtual-stature reach, physical net clearance and arrival, distinct trajectories and ground depths, full unhit live flights, translated heads, and pause preservation.");
            return checks;
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[Feed Test] " + message);
        }

        private static void TestSequences()
        {
            Require(PracticeFeed.BalancedSequence(0, 0).Length == 0, "Empty sequence failed");
            foreach (int seed in new[] { 0, 1, -1, 25054, int.MinValue })
            {
                FeedKind[] order = PracticeFeed.BalancedSequence(103, seed);
                FeedKind[] repeat = PracticeFeed.BalancedSequence(103, seed);
                int[] totals = new int[5];
                for (int i = 0; i < order.Length; i++)
                {
                    Require(order[i] == repeat[i], "Seed is not reproducible");
                    Require(i == 0 || order[i] != order[i - 1], "Repeated neighbouring feed, including block boundary");
                    totals[(int)order[i]]++;
                }
                for (int start = 0; start + 5 <= order.Length; start += 5)
                {
                    int mask = 0;
                    for (int i = 0; i < 5; i++) mask |= 1 << (int)order[start + i];
                    Require(mask == 31, "Five-feed block omitted a type");
                }
                foreach (int total in totals) Require(total == 20 || total == 21, "Mixed round is unbalanced");
            }
        }

        private static void TestProfiles()
        {
            foreach (float mass in new[] { .0045f, .0054f, .0060f })
            foreach (float height in new[] { 1.25f, 1.6f, 1.63f, 1.85f })
            foreach (int hand in new[] { -1, 1 })
            foreach (int variant in new[] { 0, 1, 2 })
            foreach (FeedKind kind in Enum.GetValues(typeof(FeedKind)))
            {
                Vector3 eye = new Vector3(0f, height, 0f);
                FeedProfile profile = PracticeFeed.Build(kind, hand, eye, 0f, .62f, variant);
                string name = kind + " eye=" + height + " mass=" + mass + " variant=" + variant + " hand=" + hand;
                Vector3 reach = profile.targetLocal - eye;
                Require(reach.z >= .4199f && reach.z <= .7501f, "Unsafe frontal reach: " + name);
                Require(Mathf.Abs(reach.x) <= .3001f && reach.x * hand > 0f, "Unsafe/cross-body lateral reach: " + name);
                Require(reach.y >= -.3501f && reach.y <= .2201f, "Unsafe overhead/low reach: " + name);
                Require(profile.sourceLocal.z > ShuttleMath.NetZ && profile.targetLocal.z < ShuttleMath.NetZ,
                    "Feed does not cross the real net: " + name);
                Require(profile.duration > .35f && profile.duration <= 3f, "Feed duration exceeds solver interval: " + name);
                var settings = new ShuttlePhysicsSettings { mass = mass };
                Trajectory flight = Trace(profile, settings);
                Require(Finite(flight.launch) && Finite(flight.arrival.position) && Finite(flight.arrival.velocity), "Non-finite feeder state: " + name);
                Require(Vector3.Distance(flight.arrival.position, profile.targetLocal) < .015f, "Solver missed mint target: " + name);
                Vector3 independentlyPredicted = ShuttleMath.Predict(profile.sourceLocal, flight.launch, profile.duration, settings);
                Require(Vector3.Distance(independentlyPredicted, flight.arrival.position) < .005f, "Predict and traced trajectory disagree: " + name);
                Require(flight.crossedNet && !flight.hitNet && flight.netHeight > BadmintonCourt.NetHeightAt(flight.netCrossing.x) + .04f,
                    "Feed hits/skips regulation net: " + name);
                Require(flight.apex < RegulationHallBuilder.HallClearHeight, "Feed exceeds training-hall height: " + name);
                Require(flight.arrival.velocity.z < -.3f && flight.arrival.velocity.y < -.5f, "Feed is not approaching player on descent: " + name);
                Require(flight.launch.magnitude < 60f && flight.arrival.velocity.magnitude < 12f, "Unmoderated feed speed: " + name);
                if (mass == .0054f && hand == 1 && variant == 1)
                    Debug.Log("[Feed Test] " + name + " duration=" + profile.duration.ToString("F2") +
                        "s arrival=" + flight.arrival.velocity.magnitude.ToString("F2") + "m/s angle=" + flight.angle.ToString("F1") +
                        "deg apex=" + flight.apex.ToString("F2") + "m net=" + flight.netHeight.ToString("F2") + "m");
            }
            // Inspector reach extremes and arbitrary head translation must not
            // send the next target behind or outside the small-space envelope.
            foreach (float baseReach in new[] { .1f, .4f, .9f, 5f })
            foreach (FeedKind kind in Enum.GetValues(typeof(FeedKind)))
            {
                Vector3 eye = new Vector3(.4f, 1.85f, -.2f);
                FeedProfile profile = PracticeFeed.Build(kind, 1, eye, 0f, baseReach, 2);
                Vector3 reach = profile.targetLocal - eye;
                Require(reach.z >= .4199f && reach.z <= .7501f && Mathf.Abs(reach.x) <= .3001f,
                    "Inspector reach bypassed stationary envelope");
            }
        }

        private static void TestFocusedDrills()
        {
            // Preserve actual floor/eye height in both specialised drills. There
            // must be no dependence on the former artificial 1.5/1.75 m eye.
            foreach(PracticeDrill drill in new[]{PracticeDrill.NetTouch,PracticeDrill.Overhead})
            foreach(float eyeHeight in new[]{1.25f,1.6f,1.63f,1.85f})
            foreach(float mass in new[]{.0045f,.0054f,.006f})
            foreach(int hand in new[]{-1,1})
            {
                bool net=drill==PracticeDrill.NetTouch;
                float playerZ=net?2.4f:1.2f;
                var eye=new Vector3(0f,eyeHeight,playerZ);
                var profile=PracticeFeed.Fixed(drill,hand,eye,playerZ,.62f,.22f);
                var settings=new ShuttlePhysicsSettings { mass=mass };
                string label=drill+" eye="+eyeHeight+" mass="+mass+" hand="+hand;
                Require(Mathf.Abs(profile.targetLocal.y-eyeHeight-(net?-.16f:.22f))<.0001f,
                    "Focused drill uses an artificial eye height: "+label);
                Require(Mathf.Abs(profile.targetLocal.z-playerZ-.62f)<.0001f,
                    "Focused drill changed actual reach: "+label);
                Trajectory flight=Trace(profile,settings);
                Require(Finite(flight.launch) && Finite(flight.arrival.position),"Focused feed is non-finite: "+label);
                Require(Vector3.Distance(flight.arrival.position,profile.targetLocal)<.015f,
                    "Focused feed misses reachable target: "+label);
                Require(flight.crossedNet && !flight.hitNet && flight.netHeight>BadmintonCourt.NetHeightAt(flight.netCrossing.x)+.04f,
                    "Focused feed does not clear regulation net: "+label);
                Require(flight.apex<RegulationHallBuilder.HallClearHeight,"Focused feed hits ceiling: "+label);
                Require(flight.arrival.velocity.z<-.1f && flight.arrival.velocity.y<-.1f,
                    "Focused feed is not approaching on descent: "+label);
                Vector3 ground; float groundTime;
                Require(TryTraceGround(profile,settings,out ground,out groundTime) && Finite(ground) && groundTime>profile.duration,
                    "Focused miss fails to reach finite ground after intercept: "+label);
                if(mass==.0054f && hand==1)
                    Debug.Log("[Feed Test] Focused "+label+" target="+profile.targetLocal+" net="+flight.netHeight.ToString("F3")+"m");
            }
        }

        private static void TestDistinctFlight()
        {
            var settings = new ShuttlePhysicsSettings();
            foreach (float height in new[] { 1.25f, 1.6f, 1.63f, 1.85f })
            {
                Vector3 eye = new Vector3(0f, height, 0f);
                FeedProfile clearProfile = PracticeFeed.Build(FeedKind.Clear, 1, eye, 0f, .62f, 1);
                FeedProfile smashProfile = PracticeFeed.Build(FeedKind.Smash, 1, eye, 0f, .62f, 1);
                FeedProfile dropProfile = PracticeFeed.Build(FeedKind.Drop, 1, eye, 0f, .62f, 1);
                FeedProfile driveProfile = PracticeFeed.Build(FeedKind.Drive, 1, eye, 0f, .62f, 1);
                FeedProfile netProfile = PracticeFeed.Build(FeedKind.NetShot, 1, eye, 0f, .62f, 1);
                Trajectory clear = Trace(clearProfile, settings), smash = Trace(smashProfile, settings),
                    drop = Trace(dropProfile, settings), drive = Trace(driveProfile, settings), net = Trace(netProfile, settings);
                Require(clearProfile.duration > dropProfile.duration + .25f && clear.apex > drop.apex + .75f,
                    "Clear is not longer/higher than drop");
                Require(clear.angle < -65f, "Clear is not falling steeply into an overhead contact");
                Require(smash.launch.y < 0f && smash.angle < drive.angle - 10f, "Smash is not a downwards, steeper attack feed");
                Require(smash.arrival.velocity.magnitude > drive.arrival.velocity.magnitude + .05f,
                    "Moderated smash is not faster than drive");
                Require(drive.angle > -30f && drive.angle < 0f, "Drive remains a lob, not a flat approaching feed");
                Require(drop.angle < drive.angle - 20f && drop.arrival.velocity.magnitude < smash.arrival.velocity.magnitude - .5f,
                    "Drop is not slower/steeper than drive and smash");
                Require(dropProfile.targetLocal.z > clearProfile.targetLocal.z + .1f,
                    "Mixed near/far contact depth has no variation");
                Require(netProfile.sourceLocal.z < 5f && net.arrival.velocity.magnitude < smash.arrival.velocity.magnitude - 1f,
                    "Net-origin practice feed is not closer/gentler");
            }
        }

        private static Trajectory Trace(FeedProfile profile, ShuttlePhysicsSettings settings)
        {
            Vector3 launch = ShuttleMath.SolveLaunch(profile.sourceLocal, profile.targetLocal, profile.duration, settings);
            ShuttleState state = ShuttleState.Launch(profile.sourceLocal, launch);
            var result = new Trajectory { launch = launch, apex = profile.sourceLocal.y, netHeight = float.NaN };
            for (float time = 0f; time < profile.duration - .000001f;)
            {
                float dt = Mathf.Min(ShuttlePhysics.Step, profile.duration - time);
                Vector3 before = state.position;
                ShuttlePhysics.Integrate(ref state, settings, dt);
                time += dt;
                result.apex = Mathf.Max(result.apex, state.position.y);
                result.hitNet |= ShuttleMath.CrossesNet(before,state.position);
                Vector3 crossing;
                if (ShuttleMath.NetCrossing(before, state.position, out crossing))
                {
                    result.crossedNet = true;
                    result.netHeight = crossing.y;
                    result.netCrossing = crossing;
                }
            }
            result.arrival = state;
            result.angle = Mathf.Atan2(state.velocity.y, new Vector2(state.velocity.x, state.velocity.z).magnitude) * Mathf.Rad2Deg;
            return result;
        }

        private static void TestGroundDepths()
        {
            var settings = new ShuttlePhysicsSettings();
            Vector3 eye = new Vector3(0f, 1.6f, 0f);
            float overallMin = float.PositiveInfinity, overallMax = float.NegativeInfinity;
            foreach (FeedKind kind in Enum.GetValues(typeof(FeedKind)))
            {
                float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
                foreach (int variant in new[] { 0, 1, 2 })
                {
                    FeedProfile profile = PracticeFeed.Build(kind, 1, eye, 0f, .62f, variant);
                    Vector3 landing;
                    float flightTime;
                    Require(TryTraceGround(profile, settings, out landing, out flightTime), "Unreturned trajectory did not reach finite ground: " + kind);
                    Require(Finite(landing) && Mathf.Abs(landing.y - .025f) < .0001f && flightTime > profile.duration,
                        "Unreturned ground trace stopped before safe interception target: " + kind);
                    minimum = Mathf.Min(minimum, landing.z);
                    maximum = Mathf.Max(maximum, landing.z);
                }
                overallMin = Mathf.Min(overallMin, minimum);
                overallMax = Mathf.Max(overallMax, maximum);
                Debug.Log("[Feed Test] Unhit ground depth " + kind + " at eye1.60m / 5.4g: local Z " +
                    minimum.ToString("F3") + " .. " + maximum.ToString("F3") + "m");
            }
            Require(overallMax - overallMin > .15f, "Mixed feed ground depths are identical despite different mint-ring targets");
            Debug.Log("[Feed Test] Mixed unhit ground-depth span=" + (overallMax - overallMin).ToString("F3") +
                "m. Negative court Z is an ignored shuttle passing the player, not a required behind-body contact.");
        }

        private static bool TryTraceGround(FeedProfile profile, ShuttlePhysicsSettings settings, out Vector3 landing, out float flightTime)
        {
            Vector3 launch = ShuttleMath.SolveLaunch(profile.sourceLocal, profile.targetLocal, profile.duration, settings);
            ShuttleState state = ShuttleState.Launch(profile.sourceLocal, launch);
            landing = state.position;
            flightTime = 0f;
            for (int step = 0; step < 3600; step++)
            {
                Vector3 before = state.position;
                ShuttlePhysics.Integrate(ref state, settings, ShuttlePhysics.Step);
                if (!Finite(state.position) || !Finite(state.velocity)) return false;
                if (state.position.y <= .025f)
                {
                    float fraction = Mathf.Clamp01((before.y - .025f) / Mathf.Max(.00001f, before.y - state.position.y));
                    landing = Vector3.Lerp(before, state.position, fraction);
                    flightTime = (step + fraction) * ShuttlePhysics.Step;
                    return true;
                }
            }
            return false;
        }

        private static void TestMixedSession(BadmintonSession session)
        {
            Transform head = session.playerCamera.transform;
            Vector3 originalPosition = head.position;
            Quaternion originalRotation = head.rotation;
            PracticeDrill originalDrill = session.drill;
            PracticeMode originalMode = session.mode;
            bool originalHeightSimulation = session.simulatePlayerHeight;
            bool originalCompact = session.compactSeatedTargets;
            float originalStature = session.simulatedStature;
            FeedPattern originalPattern = session.feedPattern;
            float originalReach = session.seatedReach;
            float originalMass = session.physics.mass;
            int originalSeed = session.feedSeed, originalLength = session.roundLength;
            MethodInfo serve = typeof(BadmintonSession).GetMethod("Serve", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(serve != null, "Mixed feeder entrypoint missing");
            try
            {
                session.mode = PracticeMode.Training;
                session.compactSeatedTargets = false;
                session.drill = PracticeDrill.Rally;
                session.feedPattern = FeedPattern.Mixed;
                session.seatedReach = .62f;
                session.physics.mass = .0054f;
                session.feedSeed = 25054;
                session.roundLength = 5;
                session.simulatedStature = 1.75f;
                foreach (bool simulate in new[] { false, true })
                foreach (float height in new[] { 1.25f, 1.6f, 1.85f })
                {
                    session.simulatePlayerHeight = simulate;
                    head.SetPositionAndRotation(new Vector3(1.2f, height, -.7f), Quaternion.Euler(0f, 45f, 0f));
                    Require(session.Recenter(), "Mixed standing/seated recenter failed");
                    session.StartPractice(XRNode.RightHand);
                    Vector3 localEye=session.court.InverseTransformPoint(head.position);
                    Require(Mathf.Abs(localEye.y-(simulate?1.63f:height))<.0001f,
                        "Mixed live feed did not preserve selected virtual/real height mode");
                    Require(Mathf.Abs(session.court.position.y-(simulate?height-1.63f:0f))<.0001f,
                        "Mixed live feed has incorrect virtual-floor offset");
                    Vector3 courtPosition = session.court.position;
                    Quaternion courtRotation = session.court.rotation;
                    Vector3 headPosition = head.position;
                    Quaternion headRotation = head.rotation;
                    FeedKind[] order = PracticeFeed.BalancedSequence(5, session.feedSeed);
                    float liveGroundMin = float.PositiveInfinity, liveGroundMax = float.NegativeInfinity;
                    for (int i = 0; i < 5; i++)
                    {
                        serve.Invoke(session, null);
                        FeedProfile profile = session.LastFeedProfile;
                        Require(session.shuttle.Flying && session.Feeds == i + 1 && profile.kind == order[i], "Live mixed order/lifecycle failed");
                        Require(Vector3.Distance(session.reachMarker.localPosition, profile.targetLocal) < .001f, "Mint ring not at actual incoming contact");
                        Vector3 physicalReach=session.court.InverseTransformDirection(session.reachMarker.position-head.position);
                        Require(physicalReach.z>=.4199f && physicalReach.z<=.7501f &&
                            Mathf.Abs(physicalReach.x)<=.3001f && physicalReach.y>=-.3501f && physicalReach.y<=.2201f,
                            "Virtual stature moved mixed target outside actual stationary hand reach");
                        Require(Vector3.Distance(session.court.InverseTransformPoint(session.shuttle.transform.position), profile.sourceLocal) < .001f,
                            "Live launch not at chosen profile source");
                        Require(Vector3.Distance(session.court.Find("Feeder Assembly").localPosition,
                            profile.sourceLocal - new Vector3(0f, 1.5f, 8.6f)) < .001f, "Visible machine not at chosen source");
                        if (i == 1)
                        {
                            session.TogglePause();
                            session.SetFeedPattern(FeedPattern.Fixed);
                            Require(session.feedPattern == FeedPattern.Mixed && session.shuttle.Flying, "Paused-flight pattern edit was not rejected");
                            session.StartPractice(XRNode.RightHand);
                            Require(Vector3.Distance(session.reachMarker.localPosition, profile.targetLocal) < .001f,
                                "Resume reset mixed mint ring to fixed target");
                        }
                        for (float time = 0f; time < profile.duration - .000001f;)
                        {
                            float dt = Mathf.Min(1f / 90f, profile.duration - time);
                            session.shuttle.AdvanceFrame(dt);
                            time += dt;
                        }
                        Require(session.shuttle.Flying && !session.shuttle.Returned, "Mixed feed collided/ended before contact");
                        Require(Vector3.Distance(session.court.InverseTransformPoint(session.shuttle.transform.position), profile.targetLocal) < .035f,
                            "Live mixed feed missed mint ring");
                        Require(session.court.position == courtPosition && Quaternion.Angle(session.court.rotation, courtRotation) < .01f,
                            "Automatic feed moved the court");
                        Require(head.position == headPosition && Quaternion.Angle(head.rotation, headRotation) < .01f,
                            "Automatic feed moved the XR camera");
                        if (Mathf.Abs(height - 1.6f) < .001f)
                        {
                            // Continue all five kinds through the normal game
                            // lifecycle: no forced Stop and no synthetic finish.
                            for (int frame = 0; frame < 900 && session.shuttle.Flying; frame++)
                                session.shuttle.AdvanceFrame(1f / 90f);
                            Require(!session.shuttle.Flying && !session.shuttle.Returned && session.Hits == 0 && session.Score == 0,
                                "Unhit live mixed feed failed to resolve as a miss: " + profile.kind);
                            Vector3 ground;
                            float flightTime;
                            Require(TryTraceGround(profile, session.physics, out ground, out flightTime), "Live ground comparison failed to terminate");
                            Vector3 marked = session.landingMarker.localPosition;
                            Require(session.landingMarker.gameObject.activeSelf && Finite(marked) &&
                                new Vector2(marked.x - ground.x, marked.z - ground.z).magnitude < .04f,
                                "Live miss landing disagrees with physical ground trace: " + profile.kind);
                            liveGroundMin = Mathf.Min(liveGroundMin, marked.z);
                            liveGroundMax = Mathf.Max(liveGroundMax, marked.z);
                            Require(session.court.position == courtPosition && head.position == headPosition,
                                "Finishing an ignored shuttle moved the player/court");
                        }
                        else
                        {
                            session.shuttle.Stop();
                            session.OnFinished("MISS", profile.targetLocal, false);
                        }
                    }
                    if (Mathf.Abs(height - 1.6f) < .001f)
                        Require(liveGroundMax - liveGroundMin > .15f, "Actual live ground landing depths have no front/back variation");
                    serve.Invoke(session, null);
                    Require(!session.Running && session.Feeds == 5, "Mixed round did not finish");
                }
            }
            finally
            {
                session.shuttle.Stop();
                head.SetPositionAndRotation(originalPosition, originalRotation);
                session.drill = originalDrill;
                session.mode = originalMode;
                session.simulatePlayerHeight = originalHeightSimulation;
                session.compactSeatedTargets = originalCompact;
                session.simulatedStature = originalStature;
                session.feedPattern = originalPattern;
                session.seatedReach = originalReach;
                session.physics.mass = originalMass;
                session.feedSeed = originalSeed;
                session.roundLength = originalLength;
                session.Recenter();
            }
        }

        private static void TestCompactProfiles()
        {
            Require(Mathf.Abs(PracticeFeed.CompactReach-.44f)<.00001f &&
                Mathf.Abs(PracticeFeed.CompactSide-.14f)<.00001f &&
                Mathf.Abs(PracticeFeed.CompactHeight-.04f)<.00001f,
                "Compact fixed defaults changed from the shared AI/feed comfort profile");
            foreach(float mass in new[]{.0045f,.0054f,.0060f})
            foreach(float height in new[]{1.25f,1.6f,1.63f,1.85f})
            foreach(int hand in new[]{-1,1})
            foreach(int variant in new[]{0,1,2})
            foreach(FeedKind kind in Enum.GetValues(typeof(FeedKind)))
            {
                Vector3 eye=new Vector3(.24f,height,-.18f);
                FeedProfile profile=PracticeFeed.Build(kind,hand,eye,0f,.62f,variant,true);
                string label="compact "+kind+" eye="+height+" mass="+mass+" hand="+hand+" variant="+variant;
                Vector3 reach=profile.targetLocal-eye;
                Require(reach.z>=.3599f && reach.z<=.5001f && reach.x*hand>=.1099f && reach.x*hand<=.1701f &&
                    reach.y>=-.0401f && reach.y<=.2001f,"Compact mixed target escaped closer/higher envelope: "+label);
                var legacy=PracticeFeed.Build(kind,hand,eye,0f,.62f,variant);
                Require(profile.targetLocal.z<legacy.targetLocal.z-.05f &&
                    Mathf.Abs(reach.x)<Mathf.Abs(legacy.targetLocal.x-eye.x),
                    "Compact profile did not reduce frontal/lateral reach: "+label);
                if(kind!=FeedKind.Clear)
                    Require(profile.targetLocal.y>legacy.targetLocal.y+.09f,"Compact low feed was not raised: "+label);
                Require(profile.kind==kind && profile.sourceLocal==legacy.sourceLocal && profile.duration==legacy.duration,
                    "Compact feed lost source/type/timing identity: "+label);
                var settings=new ShuttlePhysicsSettings { mass=mass };
                ValidateCompactFlight(profile,settings,label);
                foreach(float arbitraryReach in new[]{.1f,5f})
                {
                    var clamped=PracticeFeed.Build(kind,hand,eye,0f,arbitraryReach,variant,true);
                    Require(clamped.targetLocal==profile.targetLocal,"Legacy reach changed compact safe defaults: "+label);
                }
            }
            foreach(PracticeDrill drill in Enum.GetValues(typeof(PracticeDrill)))
            foreach(float height in new[]{1.25f,1.6f,1.63f,1.85f})
            foreach(float mass in new[]{.0045f,.0054f,.0060f})
            foreach(int hand in new[]{-1,1})
            {
                float playerZ=drill==PracticeDrill.NetTouch?2.4f:drill==PracticeDrill.Overhead?1.2f:0f;
                Vector3 eye=new Vector3(-.21f,height,playerZ+.17f);
                var profile=PracticeFeed.Fixed(drill,hand,eye,playerZ,.62f,.22f,true);
                Vector3 reach=profile.targetLocal-eye;
                float expectedHeight=drill==PracticeDrill.NetTouch?.02f:drill==PracticeDrill.Overhead?.20f:PracticeFeed.CompactHeight;
                string label="compact fixed "+drill+" eye="+height+" mass="+mass+" hand="+hand;
                Require(Vector3.Distance(reach,new Vector3(hand*PracticeFeed.CompactSide,expectedHeight,PracticeFeed.CompactReach))<.0001f,
                    "Compact fixed feed ignored translated head or changed comfortable reach: "+label);
                var arbitrary=PracticeFeed.Fixed(drill,hand,eye,playerZ,4f,2f,true);
                Require(arbitrary.targetLocal==profile.targetLocal,"Legacy Inspector values escaped compact fixed envelope: "+label);
                ValidateCompactFlight(profile,new ShuttlePhysicsSettings { mass=mass },label);
            }
            foreach(float height in new[]{1.25f,1.63f,1.85f})
            {
                Vector3 eye=new Vector3(0f,height,0f);
                var settings=new ShuttlePhysicsSettings();
                var clearProfile=PracticeFeed.Build(FeedKind.Clear,1,eye,0f,.62f,1,true);
                var smashProfile=PracticeFeed.Build(FeedKind.Smash,1,eye,0f,.62f,1,true);
                var dropProfile=PracticeFeed.Build(FeedKind.Drop,1,eye,0f,.62f,1,true);
                var driveProfile=PracticeFeed.Build(FeedKind.Drive,1,eye,0f,.62f,1,true);
                var netProfile=PracticeFeed.Build(FeedKind.NetShot,1,eye,0f,.62f,1,true);
                Trajectory clear=Trace(clearProfile,settings),smash=Trace(smashProfile,settings),
                    drop=Trace(dropProfile,settings),drive=Trace(driveProfile,settings),net=Trace(netProfile,settings);
                Require(clear.apex>drop.apex+.75f && clear.angle<-65f,"Compact clear lost its high, steep trajectory");
                Require(smash.launch.y<0f && smash.angle<drive.angle-10f &&
                    smash.arrival.velocity.magnitude>drive.arrival.velocity.magnitude+.05f,
                    "Compact defence feed lost its distinct downward/faster approach");
                Require(drive.angle>-30f && drive.angle<0f,"Compact drive became a lob");
                Require(drop.angle<drive.angle-20f && drop.arrival.velocity.magnitude<smash.arrival.velocity.magnitude-.5f,
                    "Compact drop lost its slower/steeper approach");
                Require(net.arrival.velocity.magnitude<smash.arrival.velocity.magnitude-1f &&
                    dropProfile.targetLocal.z>clearProfile.targetLocal.z+.08f,"Compact five-type depth/speed variation collapsed");
            }
        }

        private static void ValidateCompactFlight(FeedProfile profile,ShuttlePhysicsSettings settings,string label)
        {
            Trajectory flight=Trace(profile,settings);
            Require(Finite(flight.launch) && Finite(flight.arrival.position) && Finite(flight.arrival.velocity),
                "Compact flight became non-finite: "+label);
            Require(Vector3.Distance(flight.arrival.position,profile.targetLocal)<.015f,"Compact solver missed comfortable contact: "+label);
            Require(flight.crossedNet && !flight.hitNet && flight.netHeight>BadmintonCourt.NetHeightAt(flight.netCrossing.x)+.04f,
                "Compact trajectory does not clear regulation net: "+label);
            Require(flight.apex<RegulationHallBuilder.HallClearHeight && flight.launch.magnitude<60f && flight.arrival.velocity.magnitude<12f,
                "Compact trajectory exceeds hall/speed bounds: "+label);
            Require(flight.arrival.velocity.z<-.1f && flight.arrival.velocity.y<-.1f,
                "Compact trajectory does not approach on descent: "+label);
            Vector3 ground; float duration;
            Require(TryTraceGround(profile,settings,out ground,out duration) && Finite(ground) && duration>profile.duration,
                "Compact target is reached after the ground: "+label);
        }

        private static void TestCompactSession(BadmintonSession session)
        {
            Transform head=session.playerCamera.transform;
            Vector3 oldPosition=head.position;
            Quaternion oldRotation=head.rotation;
            PracticeMode oldMode=session.mode;
            PracticeDrill oldDrill=session.drill;
            FeedPattern oldPattern=session.feedPattern;
            bool oldHeight=session.simulatePlayerHeight,oldCompact=session.compactSeatedTargets;
            float oldStature=session.simulatedStature,oldMass=session.physics.mass;
            RacketController[] oldRackets=session.rackets;
            int oldLength=session.roundLength,oldSeed=session.feedSeed;
            MethodInfo serve=typeof(BadmintonSession).GetMethod("Serve",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(serve!=null,"Compact live feeder entrypoint missing");
            try
            {
                session.mode=PracticeMode.Training;
                session.compactSeatedTargets=true;
                session.simulatedStature=1.75f;
                session.feedSeed=25054;
                session.rackets=new RacketController[0];
                foreach(bool simulated in new[]{false,true})
                foreach(float actualHeight in new[]{1.25f,1.85f})
                foreach(float mass in new[]{.0045f,.0054f,.0060f})
                foreach(int hand in new[]{-1,1})
                foreach(int mode in new[]{0,1,2,3})
                {
                    session.simulatePlayerHeight=simulated;
                    session.physics.mass=mass;
                    session.feedPattern=mode==0?FeedPattern.Mixed:FeedPattern.Fixed;
                    session.drill=mode<2?PracticeDrill.Rally:mode==2?PracticeDrill.Overhead:PracticeDrill.NetTouch;
                    session.roundLength=mode==0?5:1;
                    head.SetPositionAndRotation(new Vector3(.8f,actualHeight,-.6f),Quaternion.Euler(0f,hand*45f,0f));
                    Require(session.Recenter(),"Compact live recenter failed");
                    session.StartPractice(hand<0?XRNode.LeftHand:XRNode.RightHand);
                    // Simulate a small tracked head translation after calibration.
                    // Compact profiles must stay relative to the player, not the old origin.
                    head.position+=session.court.TransformDirection(new Vector3(.10f,0f,-.08f));
                    Vector3 courtPosition=session.court.position,headPosition=head.position;
                    Quaternion courtRotation=session.court.rotation,headRotation=head.rotation;
                    for(int index=0;index<session.roundLength;index++)
                    {
                        serve.Invoke(session,null);
                        FeedProfile profile=session.LastFeedProfile;
                        string label="compact live "+session.drill+"/"+profile.kind+" mass="+mass+" simulated="+simulated+
                            " actualEye="+actualHeight+" hand="+hand;
                        Vector3 offset=session.court.InverseTransformDirection(session.reachMarker.position-head.position);
                        Require(session.shuttle.Flying && session.Feeds==index+1,"Compact serve did not launch: "+label);
                        Require(offset.z>=.3599f && offset.z<=.5001f && offset.x*hand>=.1099f && offset.x*hand<=.1701f &&
                            offset.y>=-.0401f && offset.y<=.2001f,"Compact live target escaped actual translated reach: "+label);
                        if(mode!=0)
                        {
                            float expectedHeight=session.drill==PracticeDrill.NetTouch?.02f:session.drill==PracticeDrill.Overhead?.20f:PracticeFeed.CompactHeight;
                            Require(Vector3.Distance(offset,new Vector3(hand*PracticeFeed.CompactSide,expectedHeight,PracticeFeed.CompactReach))<.0001f,
                                "Compact fixed live target differs from the comfort profile: "+label);
                        }
                        if(index==0)
                        {
                            session.TogglePause();
                            ShuttleState paused=session.shuttle.State;
                            session.shuttle.AdvanceFrame(.05f);
                            Require(session.shuttle.State.position==paused.position && session.shuttle.State.velocity==paused.velocity,
                                "Compact pause advanced ball: "+label);
                            session.StartPractice(hand<0?XRNode.LeftHand:XRNode.RightHand);
                            Require(session.reachMarker.localPosition==profile.targetLocal,"Compact resume moved comfortable target: "+label);
                        }
                        for(float time=0f;time<profile.duration-.000001f;)
                        {
                            float dt=Mathf.Min(1f/90f,profile.duration-time);
                            session.shuttle.AdvanceFrame(dt);
                            time+=dt;
                        }
                        Require(session.shuttle.Flying && !session.shuttle.Returned &&
                            Vector3.Distance(session.court.InverseTransformPoint(session.shuttle.State.position),profile.targetLocal)<.035f,
                            "Compact live feed failed to reach its physical contact point: "+label);
                        Vector3 normal=session.court.TransformDirection(new Vector3(0f,.4f,1f)).normalized;
                        int hits=session.Hits;
                        Require(session.shuttle.ReturnShot(normal*5f,normal) && session.shuttle.Returned && session.Hits==hits+1,
                            "Compact comfortable contact was not physically hittable: "+label);
                        Require(!session.shuttle.ReturnShot(normal*5f,normal),"Compact contact permitted a duplicate hit: "+label);
                        for(int frame=0;frame<900 && session.shuttle.Flying;frame++) session.shuttle.AdvanceFrame(1f/90f);
                        Require(!session.shuttle.Flying && session.Hits==hits+1,"Compact physical return did not resolve once: "+label);
                        Require(session.court.position==courtPosition && Quaternion.Angle(session.court.rotation,courtRotation)<.001f &&
                            head.position==headPosition && Quaternion.Angle(head.rotation,headRotation)<.001f,
                            "Compact feed/contact moved camera or court: "+label);
                    }
                    serve.Invoke(session,null);
                    Require(!session.Running,"Compact feed round did not end");
                }
            }
            finally
            {
                session.shuttle.Stop();
                session.mode=oldMode;
                session.drill=oldDrill;
                session.feedPattern=oldPattern;
                session.simulatePlayerHeight=oldHeight;
                session.compactSeatedTargets=oldCompact;
                session.simulatedStature=oldStature;
                session.physics.mass=oldMass;
                session.rackets=oldRackets;
                session.roundLength=oldLength;
                session.feedSeed=oldSeed;
                head.SetPositionAndRotation(oldPosition,oldRotation);
                session.Recenter();
            }
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
                !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }
    }
}
