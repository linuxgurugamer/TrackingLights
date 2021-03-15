using System;
using System.Linq;
using UnityEngine;

using LightTracker.Attributes;

namespace LightTracker
{
    public class ModuleLightTracker : PartModule
    {
        [KSPField(isPersistant = true)]
        private bool IsTracking;

        internal enum TrackMode
        {
            TargetVessel,
            ActiveVessel
        }

        [KSPField(isPersistant = true)]
        private TrackMode SelectedTrackMode;

        [KSPField(isPersistant = true)]
        private bool RestWithoutTarget;

        [KSPField(guiName = "Tracking Speed", guiActive = true, isPersistant = true), UI_FloatRange(minValue = 5f, maxValue = 180f, stepIncrement = 1f)]
        public float TrackingSpeed = 45f;

        [KSPField(guiName = "Resting Pitch", guiActive = true, isPersistant = true), UI_FloatRange(minValue = -90f, maxValue = 90f, stepIncrement = 1f, scene = UI_Scene.Flight)]
        public float RestingPitch = 0f;

        [KSPField(guiName = "Resting Yaw", guiActive = true, isPersistant = true), UI_FloatRange(minValue = -180f, maxValue = 180f, stepIncrement = 1f, scene = UI_Scene.Flight)]
        public float RestingYaw = 0f;

        [LightProperty, KSPField(guiName = "Intensity", guiActive = true, isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.05f)]
        public float LightIntensity = 1f;

        [LightProperty, KSPField(guiName = "Range", guiActive = true, isPersistant = true), UI_FloatRange(minValue = 1f, maxValue = 5f, stepIncrement = 0.05f)]
        public float LightRange = 1f;

        [LightProperty, KSPField(guiName = "Cone Size", guiActive = true, isPersistant = true), UI_FloatRange(minValue = 1f, maxValue = 80f, stepIncrement = 1f)]
        public float LightConeAngle = 30f;

        [LightProperty, KSPField(guiName = "Light R", guiActive = true, isPersistant = true), UI_FloatRange(minValue = 0.00f, maxValue = 1f, stepIncrement = 0.05f, scene = UI_Scene.Flight)]
        public float LightColorR = 1f;

        [LightProperty, KSPField(guiName = "Light G", guiActive = true, isPersistant = true), UI_FloatRange(minValue = 0.00f, maxValue = 1f, stepIncrement = 0.05f, scene = UI_Scene.Flight)]
        public float LightColorG = 1f;

        [LightProperty, KSPField(guiName = "Light B", guiActive = true, isPersistant = true), UI_FloatRange(minValue = 0.00f, maxValue = 1f, stepIncrement = 0.05f, scene = UI_Scene.Flight)]
        public float LightColorB = 1f;

        // Tracking on/off
        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        public void ToggleTracking()
        {
            IsTracking = !IsTracking;
            UpdateTrackingControl();
        }
        private void UpdateTrackingControl()
        {
            Events["ToggleTracking"].guiName = $"Tracking: {(IsTracking ? "Enabled" : "Disabled")}";
        }

        // Track mode cycle
        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        public void CycleTrackMode()
        {
            SelectedTrackMode = SelectedTrackMode.Next();
            UpdateTrackModeControl();
        }
        private void UpdateTrackModeControl()
        {
            Events["CycleTrackMode"].guiName = $"Targeting: {LabelFor(SelectedTrackMode)}";
        }
        private string LabelFor(TrackMode mode)
        {
            switch (mode)
            {
                case TrackMode.ActiveVessel:
                    return "Active vessel";
                case TrackMode.TargetVessel:
                    return "Target vessel";
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        // Rest without target on/off
        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        public void ToggleRestWithoutTarget()
        {
            RestWithoutTarget = !RestWithoutTarget;
            UpdateRestWithoutTargetControl();
        }
        private void UpdateRestWithoutTargetControl()
        {
            Events["ToggleRestWithoutTarget"].guiName = $"When no target: {(RestWithoutTarget ? "Return to rest" : "Keep orientation")}";
        }

        public override void OnStart(StartState state)
        {
            // Disable stock UI elements that are unnecessary from ModuleAnimateGeneric... assuming mouse will be used instead
            foreach (var module in part.FindModulesImplementing<ModuleAnimateGeneric>())
            {
                module.Fields["actionGUIName"].guiActive = false;
                module.Fields["startEventGUIName"].guiActive = false;
                module.Fields["endEventGUIName"].guiActive = false;
                module.Events["Toggle"].guiActive = false;
                module.Fields["status"].guiActive = false;
                module.Fields["actionGUIName"].guiActiveEditor = false;
                module.Fields["startEventGUIName"].guiActiveEditor = false;
                module.Fields["endEventGUIName"].guiActiveEditor = false;
                module.Events["Toggle"].guiActiveEditor = false;
                module.Fields["status"].guiActiveEditor = false;
            }

            // Hook up light props to change the underlying Unity light on the fly
            var lightFieldsNames = GetType()
                .GetFields()
                .Where(prop => Attribute.IsDefined(prop, typeof(LightPropertyAttribute)))
                .Select(prop => prop.Name);

            foreach (var fieldName in lightFieldsNames)
            {
                Fields[fieldName].uiControlFlight.onFieldChanged += OnLightSettingChanged;
                Fields[fieldName].uiControlEditor.onFieldChanged += OnLightSettingChanged;
            }

            UpdateTrackingControl();
            UpdateTrackModeControl();
            UpdateRestWithoutTargetControl();

            base.OnStart(state);
        }

        public override void OnStartFinished(StartState state)
        {
            ApplyLightSettings();
            base.OnStartFinished(state);
        }

        #region TrackingLogic

        // Get tracked target *position* (at which light should point)
        Vector3? GetTrackTarget(TrackMode mode)
        {
            switch (mode)
            {
                case TrackMode.ActiveVessel:
                    {
                        if (vessel == FlightGlobals.ActiveVessel)
                            return null;

                        return FlightGlobals.ActiveVessel?.transform.position;
                    }

                case TrackMode.TargetVessel:
                    {
                        if (vessel == vessel?.targetObject?.GetVessel())
                            return null;

                        return vessel?.targetObject?.GetTransform().position;
                    }

                default:
                    return null;
            }
        }
        Vector3? GetTrackTarget() => GetTrackTarget(SelectedTrackMode);

        // Get the direction for light to point at a target
        Vector3? GetTrackDirection()
        {
            Vector3? target = GetTrackTarget();
            if (target.HasValue)
            {
                Transform lightCanTransform = transform.GetChild(0).GetChild(0);
                return target - lightCanTransform.position;
            }
            return null;
        }

        // Get the direction for light to point at without target (based on settings)
        Vector3 GetRestingDirection()
        {
            Quaternion yawRotation = Quaternion.AngleAxis(RestingYaw, transform.up);
            Quaternion pitchRotation = Quaternion.AngleAxis(RestingPitch, yawRotation * transform.right);

            return pitchRotation * (yawRotation * transform.forward);
        }

        // Get current desired light forward direction (based on current setting)
        Vector3? GetTargetDirection()
        {
            if (IsTracking)
                return GetTrackDirection() ?? (RestWithoutTarget ? GetRestingDirection() : (Vector3?) null);
            else
                return GetRestingDirection();
        }

        public void LateUpdate()
        {
            if (part.vessel == null)
                return;

            var targetDir = GetTargetDirection();
            if (targetDir.HasValue)
                TurnTowards(targetDir.Value);
        }

        // Turn towards given forward direction respecting tracking speed
        private void TurnTowards(Vector3 forward)
        {
            Transform baseTransform = transform.GetChild(0);
            Transform lightCanTransform = transform.GetChild(0).GetChild(0);

            Quaternion targetRot = Quaternion.LookRotation(forward, lightCanTransform.up);
            Quaternion lookRot = Quaternion.RotateTowards(lightCanTransform.rotation, targetRot, TrackingSpeed * Time.deltaTime);

            baseTransform.rotation = lookRot;
            baseTransform.localEulerAngles = new Vector3(0, baseTransform.localEulerAngles.y, 0);

            lightCanTransform.rotation = lookRot;
            lightCanTransform.localEulerAngles = new Vector3(lightCanTransform.localEulerAngles.x, 0, 0);
        }

        #endregion
        #region LightPropertiesLogic

        private static void ApplyLightSettings(Light light, Color color, float intensity, float range, float spotAngle)
        {
            var scaledColor = new Color
            {
                r = color.r * intensity,
                g = color.g * intensity,
                b = color.b * intensity
            };
            light.range = 100 * (float) Math.Pow(range, range);
            light.color = scaledColor;
            light.spotAngle = spotAngle;
        }

        private void ApplyLightSettings(Light light)
        {
            var color = new Color
            {
                r = LightColorR,
                g = LightColorG,
                b = LightColorB
            };
            ApplyLightSettings(light, color, LightIntensity, LightRange, LightConeAngle);
        }

        // Apply prop settings to underlying Unity light
        public void ApplyLightSettings()
        {
            var light = part.GetComponentInChildren<Light>();
            ApplyLightSettings(light);
        }

        // Read RGB values from stock KSP light module and copy to local props - for tweaking in the editor
        public void ReadStockLightColorValues()
        {
            var kspLight = part.FindModuleImplementing<ModuleLight>();
            LightColorR = kspLight.lightR;
            LightColorG = kspLight.lightG;
            LightColorB = kspLight.lightB;
        }

        // Updates underlying Unity lights with selected prop values
        // In the editor RGB settings are pulled from stock KSP light module sliders
        private void OnLightSettingChanged(BaseField field, object obj)
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                ReadStockLightColorValues();

                foreach (var otherPart in part.symmetryCounterparts)
                {
                    var otherPartTracker = otherPart.FindModuleImplementing<ModuleLightTracker>();

                    otherPartTracker.ReadStockLightColorValues();
                    otherPartTracker.ApplyLightSettings();
                }
            }

            ApplyLightSettings();
        }

        #endregion
    }
}
