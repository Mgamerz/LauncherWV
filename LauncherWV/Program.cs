using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LauncherWV
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead
        );
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            uint nSize,
            out int lpNumberOfBytesWritten);


        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        public static IntPtr proc;
        public static int procID;
        public static uint PatchOffset1;
        public static uint PatchOffset2;
        public static byte[] CodeSegment;

        public static byte[] pattern = {   0x8B, 0x11, 0x8B, 0x42, 0x0C, 
                                           0x57, 0x56, 0xFF, 0xD0, 
                                           0x8B, 0xC3, // <-- move eax,ebx; offset 0x9; will be replaced with 0xB0 0x01 to get mov al,1;
                                           0x8B, 0x4D, 0xF4, 0x64, 
                                           0x89, 0x0D, 0x00, 0x00, 0x00, 
                                           0x00, 0x59, 0x5F, 0x5E, 0x5B, 
                                           0x8B, 0xE5, 0x5D, 0xC2, 0x08, 
                                           0x00, 0xCC, 0xCC, 0xCC, 0x8B, 
                                           0x41, 0x04, 0x56, 0x85, 0xC0 };
        public static byte[] pattern2 = {
                                            0x8B, 0x45, 0x0C,                       // mov     eax, [ebp+arg_4]
                                            0xC7, 0x00, 0x01, 0x00, 0x00, 0x00,     // mov     dword ptr [eax], 1
                                            0x5D,                                   // pop     ebp
                                            0xC2, 0x08, 0x00,                       // retn    8
                                            0x8B, 0x4D, 0x0C,                       // mov     ecx, [ebp+arg_4]
                                            0xC7, 0x01, 0x01, 0x00, 0x00, 0x00,     // mov     dword ptr [ecx], 1
                                            0x5D,                                   // pop     ebp
                                            0xC2, 0x08, 0x00,                       // retn    8
                                            0xCC, 0xCC, 0xCC, 0xCC, 0xCC
                                        };

        static void Main(string[] args)
        {
            Console.WriteLine("Warranty Voider's ME3 Launcher [DLC Patch + Console Patch]");
            Console.WriteLine("Modified by FemShep");
            if (!LaunchME3())
            {
                Console.WriteLine("Can't find MassEffect3.exe in same directory as this .exe.");
                System.Threading.Thread.Sleep(3000);
                return;
            } else
            {
                Console.WriteLine("Executed MassEffect3.exe. Waiting for Origin/MassEffect3 to sync.");
            }
            System.Threading.Thread.Sleep(5000);
            int attempts = 0;
            int maxattempts = 5;
            while (!FindProcess())
            {
                if (attempts > maxattempts)
                {
                    //give up
                    Console.WriteLine("MassEffect3.exe did not restart. Giving up on patching process.");
                    System.Threading.Thread.Sleep(3000);
                    return;
                }

                //try again
                attempts++;
                Console.WriteLine("MassEffect3.exe has not yet restarted. Checking again in 5 seconds (attempt "+attempts+" of "+maxattempts+").");
                System.Threading.Thread.Sleep(5000);
            }
            Console.WriteLine("MassEffect3.exe has restarted.");
            int tries = 0;
            while (true)
            {
                Console.WriteLine("Scanning for Modified DLC Offset/Console Patch Offset (attempt " + tries + ") :");
                if (DumpAndSeek() && DumpAndSeek2())
                {
                    Console.WriteLine("::::Found Offsets::::");
                    break;
                }
                else
                {
                    System.Threading.Thread.Sleep(2000);
                    tries++;
                }
                if (tries == 10)
                {
                    Console.WriteLine("Unable to find offsets, giving up. If this .exe is cracked LauncherWV may not work on it.");
                    System.Threading.Thread.Sleep(3000);
                    return;
                }
            }
            if (Check())
                Patch();
            if (Check2())
                Patch2();
            Console.WriteLine("--------------------------------");
            Console.WriteLine("Modified DLC should now verify. Console can be opened with tab or tilde or whatever your keybinds specify.");
            Console.WriteLine("LauncherWV is closing (10 seconds).");
            System.Threading.Thread.Sleep(10000);
            Environment.Exit(0);
        }

        public static bool LaunchME3()
        {
            string path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\";
            Console.WriteLine("Launching Mass Effect 3 from: "+ path + "masseffect3.exe");
            if (!File.Exists(path + "masseffect3.exe"))
                return false;
            RunShell(path + "masseffect3.exe","");
            return true;
        }

        public static bool FindProcess()
        {
            Process[] procs = Process.GetProcessesByName("masseffect3");
            if (procs.Length <= 0)
                return false;
            proc = OpenProcess(0x8 | 0x10 | 0x20, true, procs[0].Id); 
            procID = procs[0].Id;
            return true;
        }

        public static bool DumpAndSeek()
        {
            int bytesRead = 0;
            int bytesReadTotal = 0;
            Process p = Process.GetProcessById(procID);
            ProcessModule pm = p.MainModule;
            //Console.WriteLine("Base Adress : 0x" + pm.BaseAddress.ToInt32().ToString("X8") + " Name: " + pm.ModuleName + " Size: 0x" + pm.ModuleMemorySize.ToString("X8"));
            CodeSegment = new byte[pm.ModuleMemorySize];
            uint PTR = (uint)pm.BaseAddress;
            int len;
            while (bytesReadTotal != pm.ModuleMemorySize)
            {
                len = CodeSegment.Length - bytesReadTotal;
                byte[] buff = new byte[len];
                ReadProcessMemory(proc, (IntPtr)PTR, buff, len, out bytesRead);
                if (bytesRead == 0)
                {
                    Console.WriteLine("Error while reading memory");
                    return false;
                }
                for (int i = 0; i < len; i++)
                    CodeSegment[bytesReadTotal + i] = buff[i];
                bytesReadTotal += bytesRead;
                PTR += (uint)bytesRead;
            }
            //Console.WriteLine("Read bytes total : 0x" + bytesReadTotal.ToString("X8"));            
            //Console.WriteLine(" DLC Check...");
            for (int i = 0; i < bytesReadTotal - pattern.Length; i++)
            {
                if (CodeSegment[i] == pattern[0] &&
                    CodeSegment[i + 1] == pattern[1] &&
                    CodeSegment[i + 2] == pattern[2] &&
                    CodeSegment[i + 3] == pattern[3])
                    {
                        bool match = true;
                        for (int j = 0; j < pattern.Length; j++)
                            if (CodeSegment[i + j] != pattern[j])
                            {
                                match = false;
                                break;
                            };
                        if (match)
                        {
                            //Console.WriteLine("Found match @ 0x" + (i + (uint)pm.BaseAddress + 0x9).ToString("X8"));
                            PatchOffset1 = (uint)i + (uint)pm.BaseAddress + 0x9;
                            return true;
                        }
                    }
            }
            Console.WriteLine("Modified DLC offset not found. EXE may not be fully loaded yet.");
            return false;
        }

        public static bool DumpAndSeek2()
        {
            //Console.WriteLine("Searching Console Patch...");
            for (int i = 0; i < CodeSegment.Length - pattern2.Length; i++)
            {
                if (CodeSegment[i] == pattern2[0] &&
                    CodeSegment[i + 1] == pattern2[1] &&
                    CodeSegment[i + 2] == pattern2[2] &&
                    CodeSegment[i + 3] == pattern2[3])
                {
                    bool match = true;
                    for (int j = 0; j < pattern2.Length; j++)
                        if (CodeSegment[i + j] != pattern2[j])
                        {
                            match = false;
                            break;
                        };
                    if (match)
                    {
                        Process p = Process.GetProcessById(procID);
                        ProcessModule pm = p.MainModule;
                        //Console.WriteLine("Found match @ 0x" + (i + (uint)pm.BaseAddress + 0x5).ToString("X8"));
                        PatchOffset2 = (uint)i + (uint)pm.BaseAddress + 0x5;
                        return true;
                    }
                }
            }
            Console.WriteLine("Console offset not found. EXE may not be fully loaded yet.");
            return false;
        }

        public static bool Check()
        {
            uint PTR = PatchOffset1;
            byte[] buff = new byte[2];
            int bytesRead;
            ReadProcessMemory(proc, (IntPtr)PTR, buff, buff.Length, out bytesRead);
            if (bytesRead != 2)
            {
                Console.WriteLine("error: reading 2 bytes");
                return false;
            }
            UInt16 val = BitConverter.ToUInt16(buff, 0);
            //Console.WriteLine("Value is: 0x" + val.ToString("X2") + " expected : 0xC38B");
            if (!(val == 0xC38B))
            {
                Console.WriteLine("Value is not 0xC38B (mov eax,ebx)");
                return false;
            }
            return true;
        }

        public static bool Check2()
        {
            //Console.WriteLine("Offset: 0x" + PatchOffset2.ToString("X8"));
            uint PTR = PatchOffset2;
            byte[] buff = new byte[4];
            int bytesRead;
            ReadProcessMemory(proc, (IntPtr)PTR, buff, buff.Length, out bytesRead);
            if (bytesRead != 4)
            {
                Console.WriteLine("error: reading 2 bytes");
                return false;
            }
            UInt32 val = BitConverter.ToUInt32(buff, 0);
            //Console.WriteLine("Value is: 0x" + val.ToString("X2") + " expected : 0x01");
            if (!(val == 0x01))
            {
                Console.WriteLine("Value is not 0x01");
                return false;
            }
            return true;
        }

        public static void Patch()
        {
            uint PTR = PatchOffset1;
            UInt16 newval = 0x01B0;
            byte[] buff = BitConverter.GetBytes(newval);
            int bytesRead = 0;
            WriteProcessMemory(proc, (IntPtr)PTR, buff, 2, out bytesRead);
            if (bytesRead != 2)
            {
                Console.WriteLine("error: writing 2 bytes ");
                return;
            }
            ReadProcessMemory(proc, (IntPtr)PTR, buff, buff.Length, out bytesRead);
            if (bytesRead != 2)
            {
                Console.WriteLine("error: reading 2 bytes");
                return;
            }
            UInt16 val = BitConverter.ToUInt16(buff, 0);
            //Console.WriteLine("Value is: 0x" + val.ToString("X2"));
            if (!(val == newval))
                Console.WriteLine("Value is not 0x01B0 (mov al, 1)");
            else
                Console.WriteLine("Enabled modified DLC.");
            return;
        }

        public static void Patch2()
        {
            uint PTR = PatchOffset2;
            UInt32 newval = 0x00;
            byte[] buff = BitConverter.GetBytes(newval);
            int bytesRead = 0;
            WriteProcessMemory(proc, (IntPtr)PTR, buff, 4, out bytesRead);
            if (bytesRead != 4)
            {
                Console.WriteLine("error: writing 4 bytes ");
                return;
            }
            ReadProcessMemory(proc, (IntPtr)PTR, buff, buff.Length, out bytesRead);
            if (bytesRead != 4)
            {
                Console.WriteLine("error: reading 4 bytes");
                return;
            }
            UInt32 val = BitConverter.ToUInt32(buff, 0);
            //Console.WriteLine("Value is: 0x" + val.ToString("X2"));
            if (!(val == newval))
                Console.WriteLine("Value is not 0x00");
            else
                Console.WriteLine("Found console offset.");
            buff = BitConverter.GetBytes(newval);
            PTR += 0xD;
            WriteProcessMemory(proc, (IntPtr)PTR, buff, 4, out bytesRead);
            if (bytesRead != 4)
            {
                Console.WriteLine("error: writing 4 bytes ");
                return;
            }
            ReadProcessMemory(proc, (IntPtr)PTR, buff, buff.Length, out bytesRead);
            if (bytesRead != 4)
            {
                Console.WriteLine("error: reading 4 bytes");
                return;
            }
            val = BitConverter.ToUInt32(buff, 0);
            Console.WriteLine("Value is: 0x" + val.ToString("X2"));
            if (!(val == newval))
                Console.WriteLine("Value is not 0x00. Console not enabled.");
            else
                Console.WriteLine("::::Enabled the full console.::::");
            return;
        }

        public static void RunShell(string cmd, string args)
        {
            System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo(cmd, args);
            procStartInfo.WorkingDirectory = Path.GetDirectoryName(cmd) + "\\";
            procStartInfo.RedirectStandardOutput = true;
            procStartInfo.UseShellExecute = false;
            procStartInfo.CreateNoWindow = true;
            System.Diagnostics.Process proc = new System.Diagnostics.Process();
            proc.StartInfo = procStartInfo;
            proc.Start();
        }
    }
}
