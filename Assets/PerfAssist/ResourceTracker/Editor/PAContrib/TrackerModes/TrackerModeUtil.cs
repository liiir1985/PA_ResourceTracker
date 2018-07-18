using UnityEngine;
using System.Collections;
using UnityEditorInternal;
using System;
using UnityEditor;
using UnityEditor.MemoryProfiler;
using System.IO;
using MemoryProfilerWindow;
using System.Net;
using System.Collections.Generic;
using PerfAssist.LitJson;
using System.Text;

public static class TrackerModeConsts
{
    public static string[] Modes = new string[] { "Editor", "Remote", "File" };
    public static string[] ModesDesc = new string[]
    {
        "'Editor': connects to the local in-editor game (Native types only).",
        "'Remote': connects to the remote device (C# types if il2cpp is enabled).",
        "'File': opens a saved session from local file system."
    };

    public static readonly string RemoteTag = "-Remote-";
    public static readonly string EditorTag = "-Editor";

    public static readonly string SnapshotBinPostfix = ".memsnap";
    public static readonly string SnapshotJsonPostfix = ".json";
}

public static class TrackerModeUtil
{
    public static bool Handle_ServerLogging(eNetCmd cmd, UsCmd c)
    {
        UsLogPacket pkt = new UsLogPacket(c);

        string logTypeStr = "";
        switch (pkt.LogType)
        {
            case UsLogType.Error:
            case UsLogType.Exception:
            case UsLogType.Assert:
            case UsLogType.Warning:
                logTypeStr = string.Format("{1}", pkt.LogType);
                break;

            case UsLogType.Log:
            default:
                break;
        }

        string timeStr = string.Format("{0:0.00}({1})", pkt.RealtimeSinceStartup, pkt.SeqID);

        string ret = string.Format("{0} {1} <color=white>{2}</color>", timeStr, logTypeStr, pkt.Content);

        if (!string.IsNullOrEmpty(pkt.Callstack))
        {
            ret += string.Format("\n<color=gray>{0}</color>", pkt.Callstack);
        }

        Debug.Log(ret);

        return true;
    }

    public static bool SaveSnapshotFiles(string targetSession, string targetName, PackedMemorySnapshot packed, CrawledMemorySnapshot unpacked)
    {
        string targetDir = Path.Combine(MemUtil.SnapshotsDir, targetSession);
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        if (!TrackerModeUtil.SaveSnapshotBin(targetDir, targetName + TrackerModeConsts.SnapshotBinPostfix, packed))
            return false;
        /*if (!TrackerModeUtil.SaveSnapshotJson(targetDir, targetName + TrackerModeConsts.SnapshotJsonPostfix, unpacked))
            return false;*/

        Debug.LogFormat("Snapshot saved successfully. (dir: {0}, name: {1})", targetDir, targetName);
        return true;
    }

    public static PackedMemorySnapshot LoadSnapshotBin(Stream stream)
    {
        System.Reflection.ConstructorInfo ci = typeof(PackedMemorySnapshot).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new Type[0], new System.Reflection.ParameterModifier[0]);
        PackedMemorySnapshot packed = ci.Invoke(null) as PackedMemorySnapshot;
        BinaryReader br = new BinaryReader(stream);
        stream.Position += 8;
        MemUtil.LoadSnapshotProgress(0, "Loading Connection");
        float prog = 0;
        float lastProg = 0;

        var len = br.ReadInt32();
        var connctions = new Connection[len];
        System.Reflection.FieldInfo fi = typeof(PackedMemorySnapshot).GetField("m_Connections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        fi.SetValue(packed, connctions);

        for (int i = 0; i < len; i++)
        {
            Connection c = new Connection();
            c.from = br.ReadInt32();
            c.to = br.ReadInt32();
            prog = ((float)i / len) * 0.15f;
            if (prog - lastProg > 0.01)
            {
                MemUtil.LoadSnapshotProgress(prog, string.Format("Loading Connction {0}/{1}", i + 1, len));
                lastProg = prog;
            }
            connctions[i] = c;
        }

        len = br.ReadInt32();
        PackedGCHandle[] handles = new PackedGCHandle[len];
        fi = typeof(PackedMemorySnapshot).GetField("m_GcHandles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        fi.SetValue(packed, handles);
        System.Reflection.FieldInfo[] fis = new System.Reflection.FieldInfo[]
        {
            typeof(PackedGCHandle).GetField("m_Target", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        };

        for (int i = 0; i < len; i++)
        {
            object h = new PackedGCHandle();
            
            var target = br.ReadUInt64();
            fi = fis[0];
            fi.SetValue(h, target);

            prog = 0.15f + ((float)i / len) * 0.15f;
            if (prog - lastProg > 0.01)
            {
                MemUtil.LoadSnapshotProgress(prog, string.Format("Loading GCHandles {0}/{1}", i + 1, len));
                lastProg = prog;
            }
            handles[i] = (PackedGCHandle)h;
        }

        len = br.ReadInt32();
        MemorySection[] managedHeap = new MemorySection[len];
        fi = typeof(PackedMemorySnapshot).GetField("m_ManagedHeapSections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        fi.SetValue(packed, managedHeap);

        fis = new System.Reflection.FieldInfo[]
        {
           typeof(MemorySection).GetField("m_Bytes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(MemorySection).GetField("m_StartAddress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        };
        for (int i = 0; i < len; i++)
        {
            object h = new MemorySection();
            var bLen = br.ReadInt32();
            var bytes = br.ReadBytes(bLen);
            var startAddress = br.ReadUInt64();
            fi = fis[0];
            fi.SetValue(h, bytes);

            fi = fis[1];
            fi.SetValue(h, startAddress);

            prog = 0.3f + ((float)i / len) * 0.15f;
            if (prog - lastProg > 0.01)
            {
                MemUtil.LoadSnapshotProgress(prog, string.Format("Loading Managed Heap {0}/{1}", i + 1, len));
                lastProg = prog;
            }
            managedHeap[i] = (MemorySection)h;
            if (managedHeap[i].bytes == null)
            {
                UnityEngine.Debug.Log("ff");
            }
        }

        len = br.ReadInt32();
        PackedNativeUnityEngineObject[] nativeObj = new PackedNativeUnityEngineObject[len];
        fi = typeof(PackedMemorySnapshot).GetField("m_NativeObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        fi.SetValue(packed, nativeObj);

        fis = new System.Reflection.FieldInfo[]
        {
           typeof(PackedNativeUnityEngineObject).GetField("m_ClassId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(PackedNativeUnityEngineObject).GetField("m_HideFlags", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(PackedNativeUnityEngineObject).GetField("m_InstanceId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(PackedNativeUnityEngineObject).GetField("m_Flags", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(PackedNativeUnityEngineObject).GetField("m_Name", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(PackedNativeUnityEngineObject).GetField("m_NativeObjectAddress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(PackedNativeUnityEngineObject).GetField("m_Size", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        };
        for (int i = 0; i < len; i++)
        {
            object obj = new PackedNativeUnityEngineObject();
            int clsID = br.ReadInt32();
            HideFlags hideFlags = (HideFlags)br.ReadByte();
            int instanceID = br.ReadInt32();
            var ft = typeof(PackedNativeUnityEngineObject).GetNestedType("ObjectFlags", System.Reflection.BindingFlags.NonPublic);
            int flags = 0;
            if (br.ReadBoolean())
                flags |= 1;
            if (br.ReadBoolean())
                flags |= 4;
            if (br.ReadBoolean())
                flags |= 2;
            object flag = Enum.ToObject(ft, flags);
            string name = null;
            if (br.ReadBoolean())
            {
                name = br.ReadString();
            }
            long nativeAddress = br.ReadInt64();
            int size = br.ReadInt32();

            fi = fis[0];
            fi.SetValue(obj, clsID);

            fi = fis[1];
            fi.SetValue(obj, hideFlags);

            fi = fis[2];
            fi.SetValue(obj, instanceID);

            fi = fis[3];
            fi.SetValue(obj, flag);

            fi = fis[4];
            fi.SetValue(obj, name);

            fi = fis[5];
            fi.SetValue(obj, nativeAddress);

            fi = fis[6];
            fi.SetValue(obj, size);

            prog = 0.45f + ((float)i / len) * 0.15f;
            if (prog - lastProg > 0.01)
            {
                MemUtil.LoadSnapshotProgress(prog, string.Format("Loading Native Objects {0}/{1}", i + 1, len));
                lastProg = prog;
            }
            nativeObj[i] = (PackedNativeUnityEngineObject)obj;
        }

        len = br.ReadInt32();
        PackedNativeType[] nativeTypes = new PackedNativeType[len];
        fi = typeof(PackedMemorySnapshot).GetField("m_NativeTypes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        fi.SetValue(packed, nativeTypes);
        fis = new System.Reflection.FieldInfo[]
        {
           typeof(PackedNativeType).GetField("m_BaseClassId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(PackedNativeType).GetField("m_Name", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        };
        for (int i = 0; i < len; i++)
        {
            object t = new PackedNativeType();
            int baseClassId = br.ReadInt32();
            string name = null;
            if (br.ReadBoolean())
                name = br.ReadString();

            fi = fis[0];
            fi.SetValue(t, baseClassId);

            fi = fis[1];
            fi.SetValue(t, name);

            prog = 0.6f + ((float)i / len) * 0.15f;
            if (prog - lastProg > 0.01)
            {
                MemUtil.LoadSnapshotProgress(prog, string.Format("Loading Native Types {0}/{1}", i + 1, len));
                lastProg = prog;
            }
            nativeTypes[i] = (PackedNativeType)t;
        }

        len = br.ReadInt32();
        TypeDescription[] typeDesc = new TypeDescription[len];
        fi = typeof(PackedMemorySnapshot).GetField("m_TypeDescriptions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        fi.SetValue(packed, typeDesc);
        fis = new System.Reflection.FieldInfo[]
        {
           typeof(TypeDescription).GetField("m_Assembly", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_BaseOrElementTypeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_Fields", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_Flags", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_Name", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_Size", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_StaticFieldBytes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_TypeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(TypeDescription).GetField("m_TypeInfoAddress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
        };
        var fis2 = new System.Reflection.FieldInfo[]
         {
           typeof(FieldDescription).GetField("m_IsStatic", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(FieldDescription).GetField("m_Name", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(FieldDescription).GetField("m_Offset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(FieldDescription).GetField("m_TypeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
         };
        for (int i = 0; i < len; i++)
        {
            object t = new TypeDescription();
            int flags = br.ReadInt32() << 0x10;
            string assembly = br.ReadString();
            int baseOrElementTypeIndex = br.ReadInt32();
            int fLen = br.ReadInt32();
            FieldDescription[] fields = new FieldDescription[fLen];
            for(int j = 0; j < fLen; j++)
            {
                object f = new FieldDescription();
                bool isStatic = br.ReadBoolean();
                string fname = br.ReadString();
                int offset = br.ReadInt32();
                int ftypeIndex = br.ReadInt32();

                fi = fis2[0];
                fi.SetValue(f, isStatic);

                fi = fis2[1];
                fi.SetValue(f, fname);

                fi = fis2[2];
                fi.SetValue(f, offset);

                fi = fis2[3];
                fi.SetValue(f, ftypeIndex);
                fields[j] = (FieldDescription)f;
            }

            if (br.ReadBoolean())
                flags |= 2;
            if (br.ReadBoolean())
                flags |= 1;

            string name = br.ReadString();
            int size = br.ReadInt32();
            byte[] staticField = br.ReadBytes(br.ReadInt32());
            int typeIndex = br.ReadInt32();
            ulong typeAddress = br.ReadUInt64();

            fi = fis[0];
            fi.SetValue(t, assembly);
            fi = fis[1];
            fi.SetValue(t, baseOrElementTypeIndex);
            fi = fis[2];
            fi.SetValue(t, fields);
            fi = fis[3];
            fi.SetValue(t, Enum.ToObject(typeof(TypeDescription).GetNestedType("TypeFlags", System.Reflection.BindingFlags.NonPublic), flags));
            fi = fis[4];
            fi.SetValue(t, name);
            fi = fis[5];
            fi.SetValue(t, size);
            fi = fis[6];
            fi.SetValue(t, staticField);
            fi = fis[7];
            fi.SetValue(t, typeIndex);
            fi = fis[8];
            fi.SetValue(t, typeAddress);

            prog = 0.75f + ((float)i / len) * 0.15f;
            if (prog - lastProg > 0.01)
            {
                MemUtil.LoadSnapshotProgress(prog, string.Format("Loading Type Definitions {0}/{1}", i + 1, len));
                lastProg = prog;
            }
            typeDesc[i] = (TypeDescription)t;
        }

        object vminfo = new VirtualMachineInformation();
        fis = new System.Reflection.FieldInfo[]
        {
           typeof(VirtualMachineInformation).GetField("m_AllocationGranularity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(VirtualMachineInformation).GetField("m_ArrayBoundsOffsetInHeader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(VirtualMachineInformation).GetField("m_ArrayHeaderSize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(VirtualMachineInformation).GetField("m_ArraySizeOffsetInHeader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(VirtualMachineInformation).GetField("m_ObjectHeaderSize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
           typeof(VirtualMachineInformation).GetField("m_PointerSize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
        };

        fis[0].SetValue(vminfo, br.ReadInt32());
        fis[1].SetValue(vminfo, br.ReadInt32());
        fis[2].SetValue(vminfo, br.ReadInt32());
        fis[3].SetValue(vminfo, br.ReadInt32());
        int version = br.ReadInt32();
        fis[4].SetValue(vminfo, br.ReadInt32());
        fis[5].SetValue(vminfo, br.ReadInt32());
        fi = typeof(PackedMemorySnapshot).GetField("m_VirtualMachineInformation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        fi.SetValue(packed, vminfo);
        MemUtil.LoadSnapshotProgress(1f, "done");
        
        return packed;
    }

    public static bool SaveSnapshotBin(string binFilePath, string binFileName,PackedMemorySnapshot packed)
    {
        try
        {
            string fullName = Path.Combine(binFilePath, binFileName);
            if (!File.Exists(fullName))
            {
                using (Stream stream = File.Open(fullName, FileMode.Create))
                {
                    BinaryWriter bw = new BinaryWriter(stream);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("MEMSNAP\0"));
                    var connctions = packed.connections;
                    MemUtil.LoadSnapshotProgress(0, "Saving Connection");
                    float prog = 0;
                    float lastProg = 0;

                    var len = connctions.Length;
                    bw.Write(len);

                    for (int i = 0; i < len; i++)
                    {
                        var c = connctions[i];
                        bw.Write(c.from);
                        bw.Write(c.to);
                        prog = ((float)i / len) * 0.15f;
                        if (prog - lastProg > 0.01)
                        {
                            MemUtil.LoadSnapshotProgress(prog, string.Format("Saving Connction {0}/{1}", i + 1, len));
                            lastProg = prog;
                        }
                    }

                    var handles = packed.gcHandles;
                    len = handles.Length;
                    bw.Write(len);

                    for (int i = 0; i < len; i++)
                    {
                        var h = handles[i];
                        bw.Write(h.target);

                        prog = 0.15f + ((float)i / len) * 0.15f;
                        if (prog - lastProg > 0.01)
                        {
                            MemUtil.LoadSnapshotProgress(prog, string.Format("Saving GCHandles {0}/{1}", i + 1, len));
                            lastProg = prog;
                        }
                    }

                    var managedHeap = packed.managedHeapSections;
                    len = managedHeap.Length;
                    bw.Write(len);

                    for (int i = 0; i < len; i++)
                    {
                        var h = managedHeap[i];
                        bw.Write(h.bytes.Length);
                        bw.Write(h.bytes);
                        bw.Write(h.startAddress);

                        prog = 0.3f + ((float)i / len) * 0.15f;
                        if (prog - lastProg > 0.01)
                        {
                            MemUtil.LoadSnapshotProgress(prog, string.Format("Saving Managed Heap {0}/{1}", i + 1, len));
                            lastProg = prog;
                        }
                    }

                    var nativeObj = packed.nativeObjects;
                    len = nativeObj.Length;
                    bw.Write(len);

                    for (int i = 0; i < len; i++)
                    {
                        var obj = nativeObj[i];
                        bw.Write(obj.classId);
                        bw.Write((byte)obj.hideFlags);
                        bw.Write(obj.instanceId);
                        bw.Write(obj.isDontDestroyOnLoad);
                        bw.Write(obj.isManager);
                        bw.Write(obj.isPersistent);
                        bw.Write(!string.IsNullOrEmpty(obj.name));
                        if (!string.IsNullOrEmpty(obj.name))
                            bw.Write(obj.name);
                        bw.Write(obj.nativeObjectAddress);
                        bw.Write(obj.size);

                        prog = 0.45f + ((float)i / len) * 0.15f;
                        if (prog - lastProg > 0.01)
                        {
                            MemUtil.LoadSnapshotProgress(prog, string.Format("Saving Native Objects {0}/{1}", i + 1, len));
                            lastProg = prog;
                        }
                    }

                    var nativeTypes = packed.nativeTypes;
                    len = nativeTypes.Length;
                    bw.Write(len);

                    for (int i = 0; i < len; i++)
                    {
                        var t = nativeTypes[i];
                        bw.Write(t.baseClassId);
                        bw.Write(!string.IsNullOrEmpty(t.name));
                        if (!string.IsNullOrEmpty(t.name))
                            bw.Write(t.name);

                        prog = 0.6f + ((float)i / len) * 0.15f;
                        if (prog - lastProg > 0.01)
                        {
                            MemUtil.LoadSnapshotProgress(prog, string.Format("Saving Native Types {0}/{1}", i + 1, len));
                            lastProg = prog;
                        }
                    }

                    var typeDesc = packed.typeDescriptions;
                    len = typeDesc.Length;
                    bw.Write(len);

                    for (int i = 0; i < len; i++)
                    {
                        var t = typeDesc[i];
                        bw.Write(t.arrayRank);
                        bw.Write(t.assembly);
                        bw.Write(t.baseOrElementTypeIndex);
                        var fields = t.fields;
                        bw.Write(fields.Length);
                        for (int j = 0; j < fields.Length; j++)
                        {
                            var f = fields[j];
                            bw.Write(f.isStatic);
                            bw.Write(f.name);
                            bw.Write(f.offset);
                            bw.Write(f.typeIndex);
                        }
                        bw.Write(t.isArray);
                        bw.Write(t.isValueType);
                        bw.Write(t.name);
                        bw.Write(t.size);
                        bw.Write(t.staticFieldBytes.Length);
                        bw.Write(t.staticFieldBytes);
                        bw.Write(t.typeIndex);
                        bw.Write(t.typeInfoAddress);

                        prog = 0.75f + ((float)i / len) * 0.15f;
                        if (prog - lastProg > 0.01)
                        {
                            MemUtil.LoadSnapshotProgress(prog, string.Format("Saving Type Definitions {0}/{1}", i + 1, len));
                            lastProg = prog;
                        }
                    }

                    bw.Write(packed.virtualMachineInformation.allocationGranularity);
                    bw.Write(packed.virtualMachineInformation.arrayBoundsOffsetInHeader);
                    bw.Write(packed.virtualMachineInformation.arrayHeaderSize);
                    bw.Write(packed.virtualMachineInformation.arraySizeOffsetInHeader);
                    bw.Write(packed.virtualMachineInformation.heapFormatVersion);
                    bw.Write(packed.virtualMachineInformation.objectHeaderSize);
                    bw.Write(packed.virtualMachineInformation.pointerSize);
                    MemUtil.LoadSnapshotProgress(1f, "done");

                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError(string.Format("save snapshot error ! msg ={0}", ex.Message));
            Debug.LogException(ex);
            return false;
        }
    }

    public static bool SaveSnapshotJson(string jsonFilePath, string jsonFileName, CrawledMemorySnapshot unpacked)
    {
        try
        {
            string jsonContent = TrackerModeUtil.ResolvePackedForJson(unpacked);
            if (string.IsNullOrEmpty(jsonContent))
                throw new Exception("resolve failed.");

            string jsonFile = Path.Combine(jsonFilePath, jsonFileName);
            FileInfo fileInfo = new FileInfo(jsonFile);
            StreamWriter sw = fileInfo.CreateText();
            sw.Write(jsonContent);
            sw.Close();
            sw.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogError(string.Format("save text error ! msg ={0}", ex.Message));
            Debug.LogException(ex);
            return false;
        }
        return true;
    }

    public static void Connect(string ip)
    {
        //if (NetManager.Instance == null)
        //    return;

        ProfilerDriver.connectedProfiler = -1;
        //if (NetManager.Instance.IsConnected)
        //    NetManager.Instance.Disconnect();

        try
        {
            if (!MemUtil.ValidateIPString(ip))
                throw new Exception("Invaild IP");

            //if (!NetManager.Instance.Connect(ip))
            //    throw new Exception("Bad Connect");

            if (!MemUtil.IsLocalhostIP(ip))
            {
                ProfilerDriver.DirectIPConnect(ip);
                if (!MemUtil.IsProfilerConnectedRemotely)
                    throw new Exception("Bad Connect");
            }

            EditorPrefs.SetString(MemPrefs.LastConnectedIP, ip);
        }
        catch (Exception ex)
        {
            EditorWindow.focusedWindow.ShowNotification(new GUIContent(string.Format("Connecting '{0}' failed: {1}", ip, ex.Message)));
            Debug.LogException(ex);

            ProfilerDriver.connectedProfiler = -1;
            //if (NetManager.Instance.IsConnected)
            //    NetManager.Instance.Disconnect();
        }
    }

    public static string ResolvePackedForJson(CrawledMemorySnapshot packed)
    {
        if (packed == null)
            return null;
        var _unpacked = packed;
        Dictionary<string, MemType> types = new Dictionary<string, MemType>();

        foreach (ThingInMemory thingInMemory in packed.allObjects)
        {
            string typeName = MemUtil.GetGroupName(thingInMemory);
            if (typeName.Length == 0)
                continue;
            int category = MemUtil.GetCategory(thingInMemory);
            MemObject item = new MemObject(thingInMemory, _unpacked);
            MemType theType;
            if (!types.ContainsKey(typeName))
            {
                theType = new MemType();
                theType.TypeName = MemUtil.GetCategoryLiteral(thingInMemory) + typeName;
                theType.Category = category;
                theType.Objects = new List<object>();
                types.Add(typeName, theType);
            }
            else
            {
                theType = types[typeName];
            }
            theType.AddObject(item);
        }

        //协议格式:
        //Data:
        //"obj" = "TypeName,Category,Count,size"
        //"info" ="RefCount,size,InstanceName(hashCode),address,typeDescriptionIndex(hashCode)"
        //typeDescription:
        //InstanceNames:

        Dictionary<int, string> typeDescDict = new Dictionary<int, string>();
        Dictionary<int, string> instanceNameDict = new Dictionary<int, string>();
        var jsonData = new JsonData();
        foreach (var type in types)
        {
            var typeData = new JsonData();
            typeData["Obj"] = type.Key + "," + type.Value.Category + "," + type.Value.Count + "," + type.Value.Size;

            var objectDatas = new JsonData();
            foreach (var obj in type.Value.Objects)
            {
                var objectData = new JsonData();
                var memObj = obj as MemObject;
                string dataInfo;
                var instanceNameHash = memObj.InstanceName.GetHashCode();
                if (!instanceNameDict.ContainsKey(instanceNameHash))
                {
                    instanceNameDict.Add(instanceNameHash, memObj.InstanceName);
                }

                dataInfo = memObj.RefCount + "," + memObj.Size + "," + instanceNameHash;
                if (type.Value.Category == 2)
                {
                    var manged = memObj._thing as ManagedObject;
                    var typeDescriptionHash = manged.typeDescription.name.GetHashCode();
                    if (!typeDescDict.ContainsKey(typeDescriptionHash))
                    {
                        typeDescDict.Add(typeDescriptionHash, manged.typeDescription.name);
                    }
                    dataInfo += "," + Convert.ToString((int)manged.address, 16) + "," + typeDescriptionHash;
                }
                objectData["info"] = dataInfo;
                objectDatas.Add(objectData);
            }
            typeData["memObj"] = objectDatas;
            jsonData.Add(typeData);
        }
        var resultJson = new JsonData();
        resultJson["Data"] = jsonData;

        StringBuilder sb = new StringBuilder();
        foreach (var key in typeDescDict.Keys)
        {
            sb.Append("[[" + key + "]:" + typeDescDict[key] + "],");
        }
        resultJson["TypeDescs"] = sb.ToString();
        sb.Remove(0, sb.Length);

        foreach (var key in instanceNameDict.Keys)
        {
            sb.Append("[[" + key + "]:" + instanceNameDict[key] + "],");
        }
        resultJson["InstanceNames"] = sb.ToString();
        return resultJson.ToJson();
    }

}
