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
        if (!TrackerModeUtil.SaveSnapshotJson(targetDir, targetName + TrackerModeConsts.SnapshotJsonPostfix, unpacked))
            return false;

        Debug.LogFormat("Snapshot saved successfully. (dir: {0}, name: {1})", targetDir, targetName);
        return true;
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
                    bw.Write(packed.connections.Length);
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
