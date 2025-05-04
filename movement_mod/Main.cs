using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

using UnityEngine;
using DV;
using static UnityModManagerNet.UnityModManager;
using dnlib;
using System.Collections.Generic;
using VLB;

namespace movement_mod;

#if DEBUG
[EnableReloading]
#endif
public static class Main
{
	private static Harmony? harmony;
	public static ModEntry mod = null!;
	public static Settings settings = new();
	public static ModEntry.ModLogger logger
	{
		get
		{
			return mod.Logger;
		}
	}

	public static bool Load(UnityModManager.ModEntry modEntry)
	{
		try
		{
			mod = modEntry;
			modEntry.OnUnload = Unload;
			settings = Settings.Load<Settings>(modEntry);

			modEntry.OnGUI = OnGUI;
			modEntry.OnSaveGUI = OnSaveGUI;

			harmony = new Harmony(modEntry.Info.Id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());

		}
		catch (Exception ex)
		{
			modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
			harmony?.UnpatchAll(modEntry.Info.Id);
			return false;
		}

		return true;
	}

	public static bool Unload(UnityModManager.ModEntry modEntry)
	{
		harmony?.UnpatchAll(mod?.Info.Id);

		return true;
	}
	static void OnGUI(UnityModManager.ModEntry modEntry)
	{
		settings?.Draw(modEntry);
	}

	static void OnSaveGUI(UnityModManager.ModEntry modEntry)
	{
		settings?.Save(modEntry);
	}

}

public class Settings : ModSettings, IDrawable
{
	[Draw("Auto bunnyhopping")] public bool autoBhop = true;
	[Draw("sv_airaccelerate")] public float sv_accelerate = 100f;
	[Draw("max_wishspd")] public float maxWishspd = 1f;

	[Space]
	[Draw("ABH (portal/hl2)")] public bool abh = false;
	[Draw("Max ABH spd")] public float maxAbhSpeed = 250f;

	[Space]
	[Draw("Mod ground movement")] public bool walk = true;
	[Draw("Run speed")] public float runSpeed = 6f;
	[Draw("Walk speed")] public float walkSpeed = 2f;
	[Draw("Ground accelerate")] public float ground_accelerate = 20f;

	[Space]
	[Draw("Reset")] public bool reset = false;

	public override void Save(UnityModManager.ModEntry modEntry)
	{
		Save(this, modEntry);
	}

	public void OnChange()
	{
		if (reset)
		{
			autoBhop = true;
			sv_accelerate = 100f;
			maxWishspd = 1f;

			abh = false;
			maxAbhSpeed = 250f;

			walk = true;
			runSpeed = 6f;
			walkSpeed = 2f;
			ground_accelerate = 20f;

			reset = false;
		}
	}
}

public static class SvUser
{
	public static void SV_AirAccelerate(Vector2 wishveloc, ref Vector2 velocity)
	{
		float addspeed, wishspeed, wishspd, accelspeed, currentspeed;

		wishspd = wishveloc.magnitude;
		wishveloc.Normalize();
		wishspeed = wishspd;
		if (wishspd > Main.settings.maxWishspd)
			wishspd = Main.settings.maxWishspd;
		currentspeed = Vector2.Dot(velocity, wishveloc);
		addspeed = wishspd - currentspeed;
		if (addspeed <= 0)
			return;
		accelspeed = Main.settings.sv_accelerate * wishspeed * Time.deltaTime;
		if (accelspeed > addspeed)
			accelspeed = addspeed;

		velocity += accelspeed * wishveloc;
	}
}


[HarmonyPatch(typeof(LocomotionInputNonVr))]
static class LocomotionInputNonVr_Patch
{
	[HarmonyPatch(nameof(LocomotionInputNonVr.JumpRequested), MethodType.Getter), HarmonyPostfix]
	public static void JumpRequested_Postfix(ref bool __result)
	{
		if (Main.settings.autoBhop)
			__result = KeyBindings.jumpKeys.IsPressed(false);
	}

	[HarmonyPatch("AxisSmoothing"), HarmonyPostfix]
	public static void AxisSmoothing_Postfix(float targetSpeed, ref float __result)
	{
		if (!CustomFirstPersonController_Patch.previouslyGrounded || Main.settings.walk)
			__result = targetSpeed;
	}
}

[HarmonyPatch(typeof(CustomFirstPersonController))]
static class CustomFirstPersonController_Patch
{
	public static Vector2 horizontalVelocity = Vector2.zero;
	public static Vector3 velocity = Vector3.zero;
	public static bool previouslyGrounded = true;
	public static bool previouslypreviouslyGrounded = true;
	public static float lastCapsuleHeight = -1f;
	public static bool canMove = false;
	public static bool reparented = false;

	[HarmonyPatch("CanMove", MethodType.Getter), HarmonyPostfix]
	public static void CanMove_Postfix(ref bool __result)
	{
		canMove = __result;
		__result = false;
	}

	[HarmonyPatch(nameof(CustomFirstPersonController.RequestFootstepSound)), HarmonyPatch([typeof(FootstepsAudioScriptableObject.MovementType)]), HarmonyPrefix]
	public static void RequestFootstepSound_Prefix(CustomFirstPersonController __instance, FootstepsAudioScriptableObject.MovementType moveType)
	{
		if (moveType == FootstepsAudioScriptableObject.MovementType.Landing)
		{
			__instance.prevFrameLandingVelocity.x = 0;
			__instance.prevFrameLandingVelocity.z = 0;
		}
	}

	[HarmonyPatch("CharacterMovement"), HarmonyPrefix]
	public static void CharacterMovement_Prefix(CustomFirstPersonController __instance, LocomotionInputWrapper ___playerInput)
	{

		if (__instance.isRepositioning)
		{
			velocity = Vector3.zero;
			return;
		}

		else if (__instance.capsule.enabled && !reparented)
		{
			velocity = __instance.capsule.velocity;
		}

		if (reparented) reparented = false;

	}

	[HarmonyPatch("CharacterMovement"), HarmonyPostfix]
	public static void CharacterMovement_Postfix(
		CustomFirstPersonController __instance,
		Vector3 ___m_MoveDir,
		float ___m_StickToGroundForce,
		bool ___m_IsWalking,
		Vector3 ___desiredMove
	) {
		if (!__instance.capsule.enabled)
		{
			if (velocity.sqrMagnitude < float.Epsilon) return;
			__instance.capsule.enabled = true;
			___m_MoveDir = Vector3.zero;
		}

		horizontalVelocity = velocity.xz();

		bool jumping = __instance.m_Jumping || !previouslyGrounded;

		bool updateVel = true;
		float speed = (___m_IsWalking && !jumping) ? Main.settings.walkSpeed : Main.settings.runSpeed;

		if (__instance.IsCrouching)
			speed *= 0.5f;

		Vector2 wishVeloc = ___desiredMove.xz() * speed;

		if (jumping)
		{
			if (previouslyGrounded && !__instance.IsCrouching && !previouslypreviouslyGrounded)
			{
				__instance.SetCapsuleHeight(1.1f);
			}

			SvUser.SV_AirAccelerate(wishVeloc, ref horizontalVelocity);

			if (Main.settings.abh && previouslyGrounded)
			{
				if (horizontalVelocity.magnitude > speed)
				{
					float diff = horizontalVelocity.magnitude - speed;
					float sign = __instance.m_Input.y < -0.1f ? 1f : -1f;
					horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity + __instance.directionDevice.forward.xz().normalized * diff * sign, Main.settings.maxAbhSpeed);
				}
			}

		}
		else if (!__instance.underwater && Main.settings.walk)
		{
			float accel = Main.settings.ground_accelerate;
			if (wishVeloc.magnitude > 0.1f)
				accel *= (1f + 1f * (1f - Vector2.Dot(wishVeloc.normalized, horizontalVelocity.normalized)));
			
			float mag = horizontalVelocity.magnitude;
			if (mag > speed)
				accel *= mag / speed;

			horizontalVelocity = Vector2.MoveTowards(horizontalVelocity, wishVeloc, accel * Time.deltaTime);
		}
		else
		{
			updateVel = false;
			velocity = ___m_MoveDir;
		}

		if (updateVel)
		{
			velocity = new Vector3(
				horizontalVelocity.x,
				___m_MoveDir.y,
				horizontalVelocity.y
			);

			if (__instance.previouslyGrounded && !__instance.m_Jumping)
			{
				velocity.y -= ___m_StickToGroundForce;
			}

		}

		previouslypreviouslyGrounded = previouslyGrounded;
		previouslyGrounded = __instance.previouslyGrounded;
		lastCapsuleHeight = __instance.CapsuleHeight;

		if (!__instance.isRepositioning)
		{
			__instance.capsule.Move(velocity * Time.deltaTime);
		}
	}

	[HarmonyPatch("Update"), HarmonyPrefix]
	public static void Update_Prefix(CustomFirstPersonController __instance, ref bool ___crouchJumped)
	{
		if (__instance.IsCrouching && !___crouchJumped && !__instance.capsule.isGrounded)
			___crouchJumped = true;
	}
}

[HarmonyPatch(typeof(CharacterReparenting))]
public static class CharacterReparenting_Patch
{
	[HarmonyPatch(nameof(CharacterReparenting.ReparentTo)), HarmonyPostfix]
	public static void ReparentTo_Postfix(CharacterReparenting __instance, CharacterController ___charController, Transform target, bool forceReparent = false)
	{
		try
		{

			if (!forceReparent && target == __instance.transform.parent)
				return;

			bool flag1 = ___charController.transform.parent.TryGetComponent<Rigidbody>(out Rigidbody rb1);
			bool flag2 = target.TryGetComponent<Rigidbody>(out Rigidbody rb2);

			Vector3 vel1 = flag1 ? rb1.velocity : Vector3.zero;
			Vector3 vel2 = flag2 ? rb2.velocity : Vector3.zero;

			CustomFirstPersonController_Patch.velocity += vel2 - vel1;
			CustomFirstPersonController_Patch.reparented = true;
		}
		catch (Exception e)
		{
			Main.logger.LogException(e);
		}
	}
}

[HarmonyPatch(typeof(CameraSmoothing))]
public static class CameraSmoothing_Patch
{
	[HarmonyPatch("UpdateCameraSmoothing"), HarmonyPostfix]
	public static void UpdateCameraSmoothing_Postfix(
		float ___bobWalkBaseDistance,
		float ___bobRunBaseDistance,
		CustomFirstPersonController ___fpc,
		float ___bobTime,
		float ___bobDuration,
		ref float ___bobDistance
	) {
		if (___bobTime == ___bobDuration && Main.settings.walk)
		{
			___bobDistance = ___fpc.m_IsWalking ? ___bobWalkBaseDistance * Main.settings.walkSpeed : ___bobRunBaseDistance * Main.settings.runSpeed;
		}
	}
}
