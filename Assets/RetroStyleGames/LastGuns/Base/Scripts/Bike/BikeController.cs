using UnityEngine;

namespace Gadd420
{
	[RequireComponent(typeof(Rigidbody))]
	public class BicycleVehicle : MonoBehaviour
	{

		[Header("Input")]
		public bool useInputManagerIfPresent = true;
		public Input_Manager inputManager;


		[Space(20)]
		[Header("Power / Braking")]
		[SerializeField] private float motorForce = 500f;
		[SerializeField] private float brakeForce = 800f;

		[Tooltip("Local-space center of mass override (lowering Y makes the bike more stable)")]
		[SerializeField] private Transform centerOfMass;

	
		[Space(20)]
		[Header("Steering")]
		[Tooltip("Maximum steering angle at standstill")]
		[SerializeField] private float maxSteeringAngle = 30f;
	
		[Tooltip("Steering responsiveness (higher = snappier)")]
		[Range(0f, 1f)]
		[SerializeField] private float turnSmoothing = 0.5f;
	
	
		[Space(20)]
		[Header("Lean")]
		[Tooltip("Maximum lean (roll) angle in degrees")]
		[SerializeField] private float maxLeanAngle = 35f;

		[Tooltip("How quickly the bike leans into/out of turns")]
		[SerializeField] private float leanSpeed = 60f;
	
	
		[Space(20)]
		[Header("Object References")]
		[SerializeField] private Transform handleBar;
	
		[SerializeField] private WheelCollider frontWheel;
		[SerializeField] private WheelCollider rearWheel;
	
		[SerializeField] private Transform frontWheelTransform;
		[SerializeField] private Transform rearWheelTransform;
	
		[SerializeField] private Transform frontWheelSuspension;
		[SerializeField] private Transform rearWheelSuspension;
	
		[SerializeField] private Transform frontWheelOrigin;
		[SerializeField] private Transform rearWheelOrigin;
	
	
		[Space(20)]
		[Header("Info (read-only)")]
		[SerializeField] private float currentSteeringAngle;
		[SerializeField] private float currentSpeed;
	
		private float _currentLeanAngle;
	
		private Vector3 _initialHandleAngle;
		private Vector3 _initialFrontWheelSuspensionPosition;
		private Quaternion _initialRearWheelSuspensionRotation;
		private Quaternion _rotationOffset;
	
		private float _wheelBase;
	
		private Rigidbody _rb;
	
		private void Start()
		{
			frontWheel.ConfigureVehicleSubsteps(5, 12, 15);
			rearWheel.ConfigureVehicleSubsteps(5, 12, 15);

			if (useInputManagerIfPresent && !inputManager)
				inputManager = GetComponent<Input_Manager>();

			_rb = GetComponent<Rigidbody>();
			_rb.centerOfMass = centerOfMass.localPosition;
		
			_initialHandleAngle = handleBar.localEulerAngles;
			_initialFrontWheelSuspensionPosition = frontWheelSuspension.localPosition;
		
			_initialRearWheelSuspensionRotation = rearWheelSuspension.localRotation;
			
			_rotationOffset = Quaternion.Inverse(frontWheel.transform.rotation) * frontWheelTransform.parent.rotation;
		
			_wheelBase = Vector3.Distance(frontWheel.transform.position, rearWheel.transform.position);
		}

		private void FixedUpdate()
		{
			ReadInput(out float steer, out float throttle, out float brake);

			HandleEngine(throttle, brake);
			HandleSteering(steer);
			HandleLean();

			UpdateHandle();
			UpdateWheels();

			UpdateSuspension();
			currentSpeed = _rb.linearVelocity.magnitude;
		}

		// Same input pattern as BuggiController/TruckController: read from the
		// Input_Manager when present, otherwise fall back to Input_Compat.
		private void ReadInput(out float steer, out float throttle, out float brake)
		{
			if (useInputManagerIfPresent && inputManager && inputManager.enabled)
			{
				steer = Mathf.Clamp(inputManager.HzInput, -1f, 1f);
				throttle = Mathf.Clamp(inputManager.VInput, -1f, 1f);
				brake = Mathf.Clamp01(inputManager.FrontBreakInput);
			}
			else
			{
				steer = Mathf.Clamp(Input_Compat.GetHorizontal(), -1f, 1f);
				throttle = Mathf.Clamp(Input_Compat.GetVertical(), -1f, 1f);
				brake = Input_Compat.GetBrakeHeld() ? 1f : 0f;
			}
		}

		#region Engine
		private void HandleEngine(float throttle, float brake)
		{
			ApplyTorque( Mathf.Abs(throttle) > 0.01f ? throttle * motorForce: 0f);

			ApplyBraking(brakeForce * brake);
		}

		private void ApplyTorque(in float torque)
		{
			rearWheel.motorTorque = torque;
		}

		private void ApplyBraking(in float force)
		{
			frontWheel.brakeTorque = force;
			rearWheel.brakeTorque = force;
		}
		#endregion
	
		#region Steering
		private void HandleSteering(float steer)
		{
			currentSteeringAngle = Mathf.Lerp(currentSteeringAngle, maxSteeringAngle * steer, turnSmoothing);
			frontWheel.steerAngle = currentSteeringAngle;
		}
		#endregion
	
		#region Lean
		private void HandleLean()
		{
		
			float steerRad = currentSteeringAngle * Mathf.Deg2Rad;
			float targetLean = -Mathf.Atan2(currentSpeed * currentSpeed * Mathf.Tan(steerRad),
				_wheelBase * -Physics.gravity.y) * Mathf.Rad2Deg;

			targetLean = Mathf.Clamp(targetLean, -maxLeanAngle, maxLeanAngle);

			_currentLeanAngle = Mathf.MoveTowards(_currentLeanAngle, targetLean,
				leanSpeed * Time.fixedDeltaTime);

			Vector3 euler = _rb.rotation.eulerAngles;
			_rb.MoveRotation(Quaternion.Euler(euler.x, euler.y, _currentLeanAngle));
		}
		#endregion
	
		#region Handle
		private void UpdateHandle()
		{
			handleBar.localEulerAngles = new Vector3(
				_initialHandleAngle.x - currentSteeringAngle, 
				_initialHandleAngle.y, 
				_initialHandleAngle.z);
		}
		#endregion
	
		#region Wheels
		private void UpdateWheels()
		{
			UpdateSingleWheel(frontWheel, frontWheelTransform, frontWheelOrigin);
			UpdateSingleWheel(rearWheel, rearWheelTransform, rearWheelOrigin);
		}
	
		private void UpdateSingleWheel(WheelCollider wheelCollider, Transform wheelTransform, Transform wheelOrigin)
		{
			wheelCollider.GetWorldPose(out Vector3 position, out Quaternion rotation);
			wheelTransform.position = position;
		
			Quaternion delta = Quaternion.Inverse(wheelTransform.rotation) * rotation;
			if (delta.w < 0f)
			{ 
				delta.x = -delta.x;
			 	delta.w = -delta.w;
			}
			float spin = 2f * Mathf.Atan2(delta.x, delta.w) * Mathf.Rad2Deg;
			wheelTransform.Rotate(spin, 0f, 0f, Space.Self);
		
			Vector3 originInParentSpace = wheelCollider.transform.parent.InverseTransformPoint(wheelOrigin.position);
			Vector3 wheelLocalPosition = wheelCollider.transform.localPosition;
			if (Mathf.Abs(wheelLocalPosition.z - originInParentSpace.z) > 0.001f)
			{
				wheelLocalPosition.z = originInParentSpace.z;
			}
		
			wheelCollider.transform.localPosition = wheelLocalPosition;
		}
		#endregion
	
		#region Suspension
		private void UpdateSuspension()
		{
			float suspensionDistanceFront = GetSuspensionTravel(frontWheel);
			float suspensionDistanceRear = GetSuspensionTravel(rearWheel);

			frontWheelSuspension.localPosition = new Vector3(
				_initialFrontWheelSuspensionPosition.x,
				_initialFrontWheelSuspensionPosition.y + suspensionDistanceFront,
				_initialFrontWheelSuspensionPosition.z);
		
			Vector3 localRotationEulerAngles = _initialRearWheelSuspensionRotation.eulerAngles;
			localRotationEulerAngles.y += Mathf.Rad2Deg * suspensionDistanceRear;
			rearWheelSuspension.localRotation = Quaternion.Euler(localRotationEulerAngles);
		}
	
		private float GetSuspensionTravel(WheelCollider wheelCollider)
		{
			wheelCollider.GetWorldPose(out Vector3 wheelPos, out _);
			Vector3 attachPoint = wheelCollider.transform.TransformPoint(wheelCollider.center);
			float extension = Vector3.Distance(wheelPos, attachPoint) 
			                  - wheelCollider.suspensionDistance 
			                  * (1- wheelCollider.suspensionSpring.targetPosition);

			return extension;
		}
		#endregion
	}
}