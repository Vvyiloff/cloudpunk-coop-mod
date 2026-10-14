using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CloudpunkCoop.Game
{
    // MeshAnimator.UpdateTick skips pose evaluation when speed == 0 or isPaused.
    // Keep it enabled, but evaluate at zero elapsed time after seeking the wire frame.
    internal sealed class ReplicatedAnimation
    {
        private readonly MeshAnimator _animator;
        private readonly Dictionary<string, MeshAnimation> _clips = new(StringComparer.Ordinal);
        public int AppliedFrames { get; private set; }
        public int FrameChanges { get; private set; }
        public int MissingClips { get; private set; }
        public int DirectFrames {get;private set;}
        private string _lastClip;
        private int _lastFrame = -1;

        public ReplicatedAnimation(MeshAnimator animator)
        {
            _animator = animator;
            animator.audioSource = null;
            animator.OnAnimationFinished = null;
            animator.isPlayer = false;
            animator.updateWhenOffscreen = true;
            animator.playAutomatically = true;
            foreach (var property in typeof(AnimSet).GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (property.PropertyType == typeof(MeshAnimation)) Add(property.GetValue(animator.animSet) as MeshAnimation);
            if (animator.animSet.idleAnimations != null)
                foreach (var clip in animator.animSet.idleAnimations) Add(clip);
            Add(animator.fallingAnimation); Add(animator.landingAnimation);
            Add(animator.currentAnimation);
        }

        private void Add(MeshAnimation clip)
        {
            if (clip != null) _clips.TryAdd(clip.name, clip);
        }

        public void Apply(byte state, byte direction, string clipName, int frame)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                _animator.speed = 1;
                _animator.isPaused = false;
                _animator.SetStateAndDirection((MeshAnimator.State)state, (MeshAnimator.Direction)direction, 0.1f, false);
                _animator.UpdateTick(Time.time);
                return;
            }
            if (!_clips.TryGetValue(clipName, out var clip))
            {
                MissingClips++;
                throw new InvalidOperationException("Replicated animation clip unavailable: " + clipName);
            }
            int target = Math.Clamp(frame, 0, Math.Max(0, clip.GetTotalFrames() - 1));
            _animator.currentAnimation = clip;
            _animator.currentState = (MeshAnimator.State)state;
            _animator.currentDirection = (MeshAnimator.Direction)direction;
            if (_lastClip != clipName)
            {
                // IL2CPP exposes this native value type as a boxed managed object.
                // Assigning default would pass null to the native struct-copy setter.
                var crossFade = _animator.currentCrossFade;
                crossFade.Reset();
                _animator.currentCrossFade = crossFade;
            }
            _animator.speed = 1;
            _animator.isPaused = false;
            _animator.lastFrameTime = Time.time;
            _animator.SetTime((target + 0.01f) * clip.timePerFrame, true);
            if (_animator.currentFrame != target)
            {
                // Some idle/blocking clips pause native evaluation. Frame data contains
                // the baked transform pose; apply it without animation callbacks or root motion.
                var data=clip.frameData;
                var transforms=_animator.exposedTransforms;
                if(data==null || target>=data.Length || data[target]==null || data[target].exposedTransforms==null)
                    throw new InvalidOperationException("Replicated clip has no baked pose: "+clipName+" frame "+target);
                var pose=data[target].exposedTransforms;
                if(transforms==null || pose.Length!=transforms.Length)
                    throw new InvalidOperationException("Replicated clip transform layout mismatch: "+clipName);
                for(int i=0;i<pose.Length;i++) if(pose[i]!=null && transforms[i]!=null) pose[i].Apply(transforms[i]);
                _animator.currentFrame=target;DirectFrames++;
            }
            AppliedFrames++;
            if (_lastClip == clipName && _lastFrame != target) FrameChanges++;
            _lastClip = clipName; _lastFrame = target;
        }
    }
}
