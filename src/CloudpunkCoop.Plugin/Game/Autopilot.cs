using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>
    /// Flies the car to a waypoint while steering around whatever is in the way.
    ///
    /// The car starts parked nose-first in a slot between two buildings, so "clear ahead" is
    /// never true at the beginning. The first version of this only ever turned left or right
    /// when the way was blocked, which is useless in a dead end: it turned, stayed blocked,
    /// turned again, and sat there while the log said "easing left" for seventeen seconds.
    ///
    /// Two things fix that:
    ///
    ///   * A near probe as well as a far one. A 55 m ray cannot tell a tight slot from a wide
    ///     street, because in both cases it stops at the first wall it meets.
    ///   * A stuck detector. If the car is asking for throttle and is not actually moving, no
    ///     amount of steering will help, so it reverses out and tries again.
    ///
    /// Every value it produces goes into <see cref="RemoteInputOverride"/>, and the same
    /// values are published to a file for <c>tools/autopilot-driver.ps1</c>, because the
    /// vehicle only responds to real keystrokes (see <see cref="PublishCommand"/>).
    /// </summary>
    public static class Autopilot
    {
        public static bool Enabled { get; private set; }

        public static string Decision { get; private set; } = "idle";

        public static float CenterClearance { get; private set; }
        public static float LeftClearance { get; private set; }
        public static float RightClearance { get; private set; }
        public static float NearClearance { get; private set; }
        public static float DistanceToWaypoint { get; private set; }
        public static bool Arrived { get; private set; }
        public static bool Stuck { get; private set; }
        public static float ProgressPerSecond { get; private set; }

        // Far probe: how far ahead to look for a route. Long enough to commit to a turn.
        private const float FarProbeDistance = 55f;

        // Near probe: how far ahead to look before deciding this is a tight space.
        private const float NearProbeDistance = 14f;

        // Yaw offset of the side probes from the nose.
        private const float ProbeSpreadDegrees = 28f;

        private const float BlockedDistance = 38f;

        // Under this the car is in a slot and the escape rules apply.
        private const float TightDistance = 7f;

        private const float TargetAltitude = 45f;
        private const float AltitudeBand = 8f;

        private const float WaypointDistance = 1000f;
        private const float ArrivalRadius = 25f;

        // Stuck detection: throttle is being asked for, but the car is not moving.
        private const float StuckWindowSeconds = 1.5f;
        private const float StuckProgressMetres = 1.5f;
        private const float EscapeSeconds = 2.5f;

        private static Vector3 _waypoint;
        private static bool _haveWaypoint;
        private static float _startedAt = -1f;

        private static Vector3 _windowStartPosition;
        private static float _windowStartTime = -1f;
        private static float _escapeUntil = -1f;
        private static int _escapeDirection = 1;
        private static float _lastCommandWrite = -1f;

        /// <summary>
        /// Where the current decision is published for the outside driver to read.
        ///
        /// The decision cannot be applied from inside the process. A Harmony prefix on
        /// InControl's value getter does replace the number the getter returns, but the
        /// vehicle never sees it: with the override reporting full lift for 35 seconds the car
        /// did not rise a centimetre, while a real Space key lifted it 17 m in two. The game's
        /// native code reads its input without passing through the managed getter.
        ///
        /// Synthetic keystrokes with scan codes, on the other hand, do reach the car. So the
        /// policy stays here, where the raycasts and the car's transform are available, and the
        /// actuation happens outside through real keys.
        /// </summary>
        public static string CommandPath { get; private set; }

        public static void Reset()
        {
            _startedAt = -1f;
            _haveWaypoint = false;
            Arrived = false;
            Stuck = false;
            Decision = "idle";
            _windowStartTime = -1f;
            _escapeUntil = -1f;
        }

        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            Reset();
        }

        public static void Update(float now)
        {
            if (!Enabled || GameRefs.PlayerCar == null)
            {
                return;
            }

            if (_startedAt < 0f)
            {
                _startedAt = now;
            }

            unsafe
            {
                Vector3 position = GameRefs.PlayerCar.transform.position;
                Vector3 forward = GameRefs.PlayerCar.transform.forward;

                if (!_haveWaypoint)
                {
                    _waypoint = MakeWaypoint(position, forward);
                    _haveWaypoint = true;
                }

                Vector3 flatForward = Flatten(forward);
                if (flatForward == Vector3.zero)
                {
                    flatForward = new Vector3(0f, 0f, 1f);
                }

                Probe(position, flatForward, out float left, out float center, out float right, out float near);
                LeftClearance = left;
                CenterClearance = center;
                RightClearance = right;
                NearClearance = near;

                Vector3 toWaypoint = Flatten(_waypoint - position);
                DistanceToWaypoint = toWaypoint.magnitude;
                Arrived = DistanceToWaypoint < ArrivalRadius;

                float waypointSteer = 0f;
                if (DistanceToWaypoint > 1f)
                {
                    waypointSteer = SteerToward(flatForward, toWaypoint / DistanceToWaypoint);
                }

                UpdateStuck(now, position);
                ChooseMotion(now, position.y, waypointSteer, left, center, right, near);
                PublishCommand(now);
            }
        }

        /// <summary>
        /// Tracks how far the car has actually travelled over a short window while it was
        /// asking to move. Steering cannot solve a dead end; only reversing can.
        /// </summary>
        private static void UpdateStuck(float now, Vector3 position)
        {
            if (_windowStartTime < 0f)
            {
                _windowStartTime = now;
                _windowStartPosition = position;
                return;
            }

            if (now - _windowStartTime < StuckWindowSeconds)
            {
                return;
            }

            float moved = Flatten(position - _windowStartPosition).magnitude;
            ProgressPerSecond = moved / (now - _windowStartTime);

            bool wantsToMove = RemoteInputOverride.Throttle > 0.3f;
            Stuck = wantsToMove && moved < StuckProgressMetres;

            _windowStartTime = now;
            _windowStartPosition = position;

            if (Stuck && _escapeUntil < 0f)
            {
                // Back out to the side with more room, then try the route again.
                _escapeUntil = now + EscapeSeconds;
                _escapeDirection = LeftClearance > RightClearance ? -1 : 1;
            }
        }

        private static Vector3 MakeWaypoint(Vector3 position, Vector3 forward)
        {
            Vector3 heading = Flatten(forward);
            if (heading == Vector3.zero)
            {
                heading = new Vector3(0f, 0f, 1f);
            }

            Vector3 target = position + heading * WaypointDistance;
            target.y = TargetAltitude;
            return target;
        }

        private static void Probe(Vector3 position, Vector3 forward,
                                  out float left, out float center, out float right, out float near)
        {
            // Start just clear of the car's own colliders, otherwise every probe hits the vehicle.
            Vector3 origin = position + forward * 3.5f + new Vector3(0f, 1.2f, 0f);

            left = Clearance(origin, Yaw(forward, -ProbeSpreadDegrees), FarProbeDistance);
            center = Clearance(origin, forward, FarProbeDistance);
            right = Clearance(origin, Yaw(forward, ProbeSpreadDegrees), FarProbeDistance);
            near = Clearance(origin, forward, NearProbeDistance);
        }

        private static float Clearance(Vector3 origin, Vector3 direction, float distance)
        {
            try
            {
                if (Physics.Raycast(origin, direction, out RaycastHit hit, distance))
                {
                    return hit.distance;
                }
            }
            catch (Exception)
            {
                // A raycast failure must not stop the autopilot; report it as open space.
            }

            return distance;
        }

        private static void ChooseMotion(float now, float altitude, float waypointSteer,
                                         float left, float center, float right, float near)
        {
            float ascend = AltitudeCommand(altitude);
            float sink = altitude > TargetAltitude + AltitudeBand ? 0.5f : 0f;

            float throttle = 0f;
            float brake = 0f;
            float steer = 0f;

            if (_escapeUntil > 0f && now < _escapeUntil)
            {
                Decision = "stuck: reversing out";
                throttle = 0f;
                brake = 1f;
                steer = _escapeDirection;
                ascend = 0.6f;
            }
            else
            {
                if (_escapeUntil > 0f)
                {
                    _escapeUntil = -1f;
                    Stuck = false;
                }

                if (Arrived)
                {
                    Decision = "arrived, holding altitude";
                    ascend = 0.35f;
                }
                else if (near < TightDistance)
                {
                    // A slot. Reverse out unless one side is genuinely open.
                    if (left > right + 3f && left > 12f)
                    {
                        Decision = "tight, easing left";
                        throttle = 0.45f;
                        steer = -1f;
                    }
                    else if (right > left + 3f && right > 12f)
                    {
                        Decision = "tight, easing right";
                        throttle = 0.45f;
                        steer = 1f;
                    }
                    else
                    {
                        Decision = "dead end: backing out";
                        throttle = 0f;
                        brake = 1f;
                        steer = left > right ? -1f : 1f;
                        ascend = 0.6f;
                    }
                }
                else if (center > BlockedDistance)
                {
                    Decision = "clear ahead, tracking waypoint";
                    throttle = 1f;
                    steer = waypointSteer;
                }
                else if (left > right + 12f)
                {
                    Decision = "blocked ahead, turning left";
                    throttle = 0.7f;
                    steer = -1f;
                }
                else if (right > left + 12f)
                {
                    Decision = "blocked ahead, turning right";
                    throttle = 0.7f;
                    steer = 1f;
                }
                else
                {
                    Decision = "boxed in, climbing";
                    throttle = 0.2f;
                    steer = waypointSteer * 0.5f;
                    ascend = 1f;
                }
            }

            RemoteInputOverride.Throttle = throttle;
            RemoteInputOverride.Brake = brake;
            RemoteInputOverride.Steer = steer;
            RemoteInputOverride.AscendValue = ascend;
            RemoteInputOverride.SinkValue = sink;
            RemoteInputOverride.StrafeLeftValue = 0f;
            RemoteInputOverride.StrafeRightValue = 0f;
        }

        private static float AltitudeCommand(float altitude)
        {
            if (altitude < TargetAltitude - AltitudeBand)
            {
                return 1f;
            }

            // A little lift keeps the hover car off its docking magnets, which is what
            // actually releases it for horizontal motion.
            return 0.35f;
        }

        private static float SteerToward(Vector3 forward, Vector3 target)
        {
            float cross = forward.z * target.x - forward.x * target.z;
            float dot = forward.x * target.x + forward.z * target.z;
            float angle = (float)(Math.Atan2(cross, dot) * 180.0 / Math.PI);

            float steer = angle / 40f;
            return steer < -1f ? -1f : steer > 1f ? 1f : steer;
        }

        private static void EnsureCommandPath()
        {
            if (CommandPath != null)
            {
                return;
            }

            try
            {
                CommandPath = Path.Combine(BepInEx.Paths.ConfigPath, "autopilot-command.txt");
            }
            catch (Exception)
            {
                CommandPath = "autopilot-command.txt";
            }
        }

        private static void PublishCommand(float now)
        {
            if (now - _lastCommandWrite < 0.05f)
            {
                return;
            }
            _lastCommandWrite = now;

            EnsureCommandPath();
            try
            {
                string text =
                    "enabled=" + (Enabled ? "1" : "0") +
                    ";throttle=" + RemoteInputOverride.Throttle.ToString("0.00", CultureInfo.InvariantCulture) +
                    ";brake=" + RemoteInputOverride.Brake.ToString("0.00", CultureInfo.InvariantCulture) +
                    ";steer=" + RemoteInputOverride.Steer.ToString("0.00", CultureInfo.InvariantCulture) +
                    ";ascend=" + RemoteInputOverride.AscendValue.ToString("0.00", CultureInfo.InvariantCulture) +
                    ";sink=" + RemoteInputOverride.SinkValue.ToString("0.00", CultureInfo.InvariantCulture) +
                    ";wp=" + DistanceToWaypoint.ToString("0", CultureInfo.InvariantCulture) +
                    ";decision=" + Decision;

                File.WriteAllText(CommandPath, text);
            }
            catch (Exception)
            {
                // A failed publish must not take the autopilot down; the driver keeps its last
                // key state until the next successful write.
            }
        }

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 Yaw(Vector3 v, float degrees)
        {
            float r = degrees * (float)Math.PI / 180f;
            float c = (float)Math.Cos(r);
            float s = (float)Math.Sin(r);
            return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
        }
    }
}
