﻿using System;

namespace FusionTweaker
{
	/// <summary>
	/// Represents the interesting MSR settings of a CPU core's P-state.
	/// </summary>
	public struct PStateMsr
	{
		/// <summary>
		/// Core multiplicator (4, 4.5, 5, ..., 31.5).
		/// </summary>
		public double CPUMultNBDivider { get; set; }

		/// <summary>
		/// Core voltage ID (0.0125 ... 1.55V).
		/// </summary>
		public double Vid { get; set; }

		/// <summary>
		/// Bus speed (0 ... 200MHz).
		/// </summary>
		public double FSB { get; set; }

        /// <summary>
        /// Core / GPU frequency.
        /// </summary>
        public double PLL { get; set; }


		/// <summary>
		/// Loads a core's P-state.
		/// </summary>
		public static PStateMsr Load(int pStateIndex, int coreIndex)
		{
			if (pStateIndex < 0 || pStateIndex > 9)
				throw new ArgumentOutOfRangeException("pStateIndex");

            uint lower = 0;
            //branch here for CPU- and NB-Pstates 
            if (pStateIndex < 8)
            {
                lower = (uint)(Program.Ols.ReadMsr(0xC0010064u + (uint)pStateIndex, coreIndex) & 0xFFFFFFFFu);
            }
            else if (pStateIndex == 8)
            {
                // value of interest: F3xDC NbPstate0Register
                lower = Program.Ols.ReadPciConfig(0xC3, 0xDC);
            }
            else if (pStateIndex == 9)
            {
                // value of interest: F6x90 NbPstate1Register
                lower = Program.Ols.ReadPciConfig(0xC6, 0x90);
            }  
			return Decode(lower,pStateIndex);
		}

        public static PStateMsr Decode(uint value, int pstate)
		{
            //uint maxDiv = (uint)K10Manager.MaxCOF();
            uint maxDiv = (uint)K10Manager.CurrCOF();
            uint fsb = (uint)K10Manager.GetBIOSBusSpeed();
                
            if (pstate < 8)
            {
                uint cpuDid = (value >> 0) & 0x0F;
                uint cpuFid = (value >> 4) & 0x1F;
                uint cpuVid = (value >> 9) & 0x7F;
                double Did = 1;

                switch (cpuDid)
                {
                    case 0:
                        Did = 1;
                        break;
                    case 1:
                        Did = 1.5;
                        break;
                    case 2:
                        Did = 2;
                        break;
                    case 3:
                        Did = 3;
                        break;
                    case 4:
                        Did = 4;
                        break;
                    case 5:
                        Did = 6;
                        break;
                    case 6:
                        Did = 8;
                        break;
                    case 7:
                        Did = 12;
                        break;
                    case 8:
                        Did = 16;
                        break;
                    default:
                        throw new NotSupportedException("This Divider is not supported");
                } 
                double Mult = (cpuFid + 16) / Did;
                var msr = new PStateMsr()
                {
                    CPUMultNBDivider = Mult,
                    Vid = 1.55 - 0.0125 * cpuVid,
                    FSB = fsb,
                    PLL = Mult * fsb
                };
                return msr;
            }
            else if (pstate == 8)
            {
                uint nclk = ((value >> 20) & 0x7F);
                uint nbVid = ((value >> 12) & 0x7F);
                double nclkdiv = 1;
                //NCLK Div 2-16 ind 0.25 steps / Div 16-32 in 0.5 steps / Div 32-63 in 1.0 steps
                if (nclk >= 8 && nclk <= 63) nclkdiv = nclk * 0.25;
                else if (nclk >= 64 && nclk <= 95) nclkdiv = (nclk - 64) * 0.5 - 16;
                else if (nclk >= 96 && nclk <= 127) nclkdiv = nclk - 64;
                else nclkdiv = 1;
                var msr = new PStateMsr()
                {
                    CPUMultNBDivider = nclkdiv,
                    Vid = 1.55 - 0.0125 * nbVid,
                    FSB = fsb,
                    PLL = (16 + maxDiv) / nclkdiv * fsb
                };
                return msr;
            }
            else if (pstate == 9)
            {
                uint nclk = ((value >> 0) & 0x7F);
                uint nbVid = ((value >> 8) & 0x7F);
                double nclkdiv = 1;
                //NCLK Div 2-16 ind 0.25 steps / Div 16-32 in 0.5 steps / Div 32-63 in 1.0 steps
                if (nclk >= 8 && nclk <= 63) nclkdiv = nclk * 0.25;
                else if (nclk >= 64 && nclk <= 95) nclkdiv = (nclk - 64) * 0.5 - 16;
                else if (nclk >= 96 && nclk <= 127) nclkdiv = nclk - 64;
                else nclkdiv = 1;
                var msr = new PStateMsr()
                {
                    CPUMultNBDivider = nclkdiv,
                    Vid = 1.55 - 0.0125 * nbVid,
                    FSB = fsb,
                    PLL = (16 + maxDiv) / nclkdiv * fsb
                };
                return msr;
            }
            else
            {
                var msr = new PStateMsr()
                {
                    CPUMultNBDivider = 0,
                    Vid = 1,
                    FSB = 100,
                    PLL = 1600
                };
                return msr;
            }
		}

		/// <summary>
		/// Encodes the settings into the 32 lower bits of a MSR.
		/// </summary>
		public uint Encode(int pstate)
		{
            if (pstate < 8)
            {
                if (CPUMultNBDivider < 4 || CPUMultNBDivider > 48) throw new ArgumentOutOfRangeException("CPUMultNBDivider");
                if (Vid <= 0 || Vid > 1.55) throw new ArgumentOutOfRangeException("Vid");
                if (FSB <= 0 || FSB > 200) throw new ArgumentOutOfRangeException("FSB");

                uint cpuFid, cpuDid;
                if (CPUMultNBDivider >= 19)
                {
                    cpuFid = (uint)Math.Abs(CPUMultNBDivider - 16);
                    cpuDid = 0; //Div 1
                }
                else if (CPUMultNBDivider == 18) //PState 4
                {
                    cpuFid = 27 - 16;
                    cpuDid = 1; //Div 1.5
                }
                else if (CPUMultNBDivider == 17) 
                {
                    cpuFid = 34 - 16;
                    cpuDid = 2; //Div 2
                }
                else if (CPUMultNBDivider == 16)
                {
                    cpuFid = 32 - 16;
                    cpuDid = 2; //Div 2
                }
                else if (CPUMultNBDivider == 15)
                {
                    cpuFid = 30 - 16;
                    cpuDid = 2; //Div 2
                }
                else if (CPUMultNBDivider == 14) //PState 5
                {
                    cpuFid = 28 - 16;
                    cpuDid = 2; //Div 2
                }
                else if (CPUMultNBDivider == 13)
                {
                    cpuFid = 26 - 16;
                    cpuDid = 2; //Div 2
                }
                else if (CPUMultNBDivider == 12)  
                {
                    cpuFid = 24 - 16;
                    cpuDid = 2; //Div 2
                }
                else if (CPUMultNBDivider == 11) //PState 6
                {
                    cpuFid = 22 - 16;
                    cpuDid = 2; //Div 2
                }
                else if (CPUMultNBDivider == 10) 
                {
                    cpuFid = 30 - 16;
                    cpuDid = 3; //Div 3
                }
                else if (CPUMultNBDivider == 9)
                {
                    cpuFid = 27 - 16;
                    cpuDid = 3; //Div 3
                }
                else if (CPUMultNBDivider == 8) //PState 7
                {
                    cpuFid = 24 - 16;
                    cpuDid = 3; //Div 3
                }
                else if (CPUMultNBDivider == 7)
                {
                    cpuFid = 21 - 16;
                    cpuDid = 3; //Div 3
                }
                else if (CPUMultNBDivider == 6)
                {
                    cpuFid = 24 - 16;
                    cpuDid = 4; //Div 4
                }
                else if (CPUMultNBDivider == 5)
                {
                    cpuFid = 20 - 16;
                    cpuDid = 4; //Div 4
                }
                else if (CPUMultNBDivider == 4)
                {
                    cpuFid = 24 - 16;
                    cpuDid = 5; //Div 6
                }
                else
                {
                    cpuFid = 24 - 16;
                    cpuDid = 3; //Div 3
                }

                
                uint cpuVid = (uint)Math.Round((1.55 - Vid) / 0.0125);
                return (cpuVid << 9) | (cpuFid << 4) | cpuDid;
            }
            else if (pstate == 8)
            {
                //K10Manager.SetBIOSBusSpeed((uint)FSB);
                uint nbVid = (uint)Math.Round((1.55 - Vid) / 0.0125);
                //CPUMultNBDivider
                //NCLK Div 2-16 ind 0.25 steps / Div 16-32 in 0.5 steps / Div 32-63 in 1.0 steps
                uint nclk = (uint)Math.Round(CPUMultNBDivider * 4);

                return (nclk << 20) | (nbVid << 12);
            }
            else if (pstate == 9)
            {
                //K10Manager.SetBIOSBusSpeed((uint)FSB);
                uint nbVid = (uint)Math.Round((1.55 - Vid) / 0.0125);
                //CPUMultNBDivider
                //NCLK Div 2-16 ind 0.25 steps / Div 16-32 in 0.5 steps / Div 32-63 in 1.0 steps
                uint nclk = (uint)Math.Round(CPUMultNBDivider * 4);

                return (nbVid << 8) | (nclk << 0);
            }
            else
            {
                return 0;
            }
		}
	}
}