using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gadd420
{

    public class Input_Manager : MonoBehaviour
    {

        public bool combineLeanAndSteering;
        public bool combineBrakeInputs;

        public float inputSmoothingTime = 0.5f;

        float vInputTime;
        float hzInputTime;

        //A&D
        protected float hzInput;
        public float HzInput
        {
            get { return hzInput; }
        }

        //W&S
        protected float vInput;
        public float VInput
        {
            get { return vInput; }
        }

        //LMouse & RMouse
        protected float leanInput;
        public float LeanInput
        {
            get { return leanInput; }
        }

        //LShift && LCtrl
        protected float wheelieInput;
        public float WheelieInput
        {
            get { return wheelieInput; }
        }

        //FrontBreak
        protected float frontBreakInput;
        public float FrontBreakInput
        {
            get { return frontBreakInput; }
        }

        private void Start()
        {
            vInput = 0;
            hzInput = 0;
        }

        void Update()
        {
            VerticalInput();
            HZInput();
            GetLeanValue();
            GetLeanBackValue();
            FrontBreak();
        }



        protected virtual void VerticalInput()
        {
            vInputTime = Mathf.Clamp(vInputTime, 0, inputSmoothingTime);

            bool forward = Input_Compat.GetForwardHeld();
            bool back = Input_Compat.GetBackwardHeld();

            if (forward || back)
            {
                if (forward)
                {
                    if (vInput < 0)
                    {
                        vInputTime -= 2 * Time.deltaTime;
                        vInput = -Mathf.InverseLerp(0, inputSmoothingTime, vInputTime);
                    }
                    else
                    {
                        vInputTime += 1 * Time.deltaTime;
                        vInput = Mathf.InverseLerp(0, inputSmoothingTime, vInputTime);
                    }
                }

                if (back)
                {
                    if (vInput > 0.01f)
                    {
                        vInputTime -= 2 * Time.deltaTime;
                        vInput = Mathf.InverseLerp(0, inputSmoothingTime, vInputTime);
                    }
                    else
                    {
                        vInputTime += 1 * Time.deltaTime;
                        vInput = -Mathf.InverseLerp(0, inputSmoothingTime, vInputTime);
                    }
                }
            }
            else
            {
                if (vInputTime > 0.01f)
                {
                    vInputTime -= 1 * Time.deltaTime;
                    if (vInput < 0)
                        vInput = -Mathf.InverseLerp(0, inputSmoothingTime, vInputTime);
                    if (vInput > 0)
                        vInput = Mathf.InverseLerp(0, inputSmoothingTime, vInputTime);
                }
                else
                {
                    vInputTime = 0;
                    vInput = 0;
                }
            }
        }


        protected virtual void HZInput()
        {
            hzInputTime = Mathf.Clamp(hzInputTime, 0, inputSmoothingTime);

            bool right = Input_Compat.GetRightHeld();
            bool left = Input_Compat.GetLeftHeld();

            if (right || left)
            {
                if (right)
                {
                    if (hzInput < 0)
                    {
                        hzInputTime -= 2 * Time.deltaTime;
                        hzInput = -Mathf.InverseLerp(0, inputSmoothingTime, hzInputTime);
                    }
                    else
                    {
                        hzInputTime += 1 * Time.deltaTime;
                        hzInput = Mathf.InverseLerp(0, inputSmoothingTime, hzInputTime);
                    }
                }

                if (left)
                {
                    if (hzInput > 0.01f)
                    {
                        hzInputTime -= 2 * Time.deltaTime;
                        hzInput = Mathf.InverseLerp(0, inputSmoothingTime, hzInputTime);
                    }
                    else
                    {
                        hzInputTime += 1 * Time.deltaTime;
                        hzInput = -Mathf.InverseLerp(0, inputSmoothingTime, hzInputTime);
                    }
                }
            }
            else
            {
                if (hzInputTime > 0.01f)
                {
                    hzInputTime -= 1 * Time.deltaTime;
                    if (hzInput < 0)
                        hzInput = -Mathf.InverseLerp(0, inputSmoothingTime, hzInputTime);
                    if (hzInput > 0)
                        hzInput = Mathf.InverseLerp(0, inputSmoothingTime, hzInputTime);
                }
                else
                {
                    hzInputTime = 0;
                    hzInput = 0;
                }
            }
        }


        protected virtual void GetLeanValue()
        {
            if (!combineLeanAndSteering)
                leanInput = Input_Compat.GetLeanAxis();
            else
                leanInput = hzInput;
        }


        protected virtual void GetLeanBackValue()
        {
            wheelieInput = Input_Compat.GetWheelieAxis();
        }


        protected virtual void FrontBreak()
        {
            frontBreakInput = Input_Compat.GetBrakeHeld() ? 1f : 0f;
        }

    }
}
