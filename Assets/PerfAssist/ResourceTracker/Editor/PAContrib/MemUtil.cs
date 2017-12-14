using MemoryProfilerWindow;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEditor.MemoryProfiler;
using UnityEngine;

public class MemUtil 
{
    public static string SnapshotsDir = string.Format("{0}/mem_snapshots", Application.persistentDataPath);

    public static string GetFullpath(string filename)
    {
        return string.IsNullOrEmpty(filename) ? "" : string.Format("{0}/{1}", SnapshotsDir, filename);
    }

    public static string[] GetFiles()
    {
        try
        {
            if (!Directory.Exists(SnapshotsDir))
            {
                Directory.CreateDirectory(SnapshotsDir);
            }

            string[] files = Directory.GetFiles(SnapshotsDir);
            for (int i = 0; i < files.Length; i++)
            {
                int begin = files[i].LastIndexOfAny(new char[] { '\\', '/' });
                if (begin != -1)
                {
                    files[i] = files[i].Substring(begin + 1);
                }
            }
            return files;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return new string[] { };
        }

    }

    public static PackedMemorySnapshot Load(string filename)
    {
        try
        {
            if (string.IsNullOrEmpty(filename))
                throw new Exception("bad_load: filename is empty.");

            string fullpath = GetFullpath(filename);
            if (!File.Exists(fullpath))
                throw new Exception(string.Format("bad_load: file not found. ({0})", fullpath));

            using (Stream stream = File.Open(fullpath, FileMode.Open))
            {
                return new BinaryFormatter().Deserialize(stream) as PackedMemorySnapshot;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogErrorFormat("bad_load: exception occurs while loading '{0}'.", filename);
            Debug.LogException(ex);
            return null;
        }
    }

    public static string Save(PackedMemorySnapshot snapshot, string filename = null)
    {
        try
        {
            if (string.IsNullOrEmpty(filename))
                filename = GetFullpath(string.Format("{0}-{1}.memsnap",
                    SysUtil.FormatDateAsFileNameString(DateTime.Now),
                    SysUtil.FormatTimeAsFileNameString(DateTime.Now)));

            //System.Runtime.Serialization.Formatters.Binary.BinaryFormatter bf = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            using (Stream stream = File.Open(filename, FileMode.Create))
            {
                BinaryWriter bw = new BinaryWriter(stream);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("MEMSNAP\0"));
                bw.Write(snapshot.connections.Length);
                var connctions = snapshot.connections;
                LoadSnapshotProgress(0, "Saving Connection");
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
                    if (prog -lastProg > 0.01)
                    {
                        LoadSnapshotProgress(prog, string.Format("Saving Connction {0}/{1}", i + 1, len));
                        lastProg = prog;
                    }
                }

                var handles = snapshot.gcHandles;
                len = handles.Length;
                bw.Write(len);

                for (int i = 0; i < len; i++)
                {
                    var h = handles[i];
                    bw.Write(h.target);

                    prog = 0.15f + ((float)i / len) * 0.15f;
                    if (prog - lastProg > 0.01)
                    {
                        LoadSnapshotProgress(prog, string.Format("Saving GCHandles {0}/{1}", i + 1, len));
                        lastProg = prog;
                    }
                }

                var managedHeap = snapshot.managedHeapSections;
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
                        LoadSnapshotProgress(prog, string.Format("Saving Managed Heap {0}/{1}", i + 1, len));
                        lastProg = prog;
                    }
                }

                var nativeObj = snapshot.nativeObjects;
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
                        LoadSnapshotProgress(prog, string.Format("Saving Native Objects {0}/{1}", i + 1, len));
                        lastProg = prog;
                    }
                }

                var nativeTypes = snapshot.nativeTypes;
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
                        LoadSnapshotProgress(prog, string.Format("Saving Native Types {0}/{1}", i + 1, len));
                        lastProg = prog;
                    }
                }

                var typeDesc = snapshot.typeDescriptions;
                len = typeDesc.Length;

                for (int i = 0; i < len; i++)
                {
                    var t = typeDesc[i];
                    bw.Write(t.arrayRank);
                    bw.Write(t.assembly);
                    bw.Write(t.baseOrElementTypeIndex);
                    var fields = t.fields;
                    bw.Write(fields.Length);
                    for(int j = 0; j < fields.Length; j++)
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
                        LoadSnapshotProgress(prog, string.Format("Saving Type Definitions {0}/{1}", i + 1, len));
                        lastProg = prog;
                    }
                }

                bw.Write(snapshot.virtualMachineInformation.allocationGranularity);
                bw.Write(snapshot.virtualMachineInformation.arrayBoundsOffsetInHeader);
                bw.Write(snapshot.virtualMachineInformation.arrayHeaderSize);
                bw.Write(snapshot.virtualMachineInformation.arraySizeOffsetInHeader);
                bw.Write(snapshot.virtualMachineInformation.heapFormatVersion);
                bw.Write(snapshot.virtualMachineInformation.objectHeaderSize);
                bw.Write(snapshot.virtualMachineInformation.pointerSize);
                LoadSnapshotProgress(1f, "done");

                //bf.Serialize(stream, snapshot);
            }
            return filename;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return "";
        }
    }

    public static string GetGroupName(ThingInMemory thing)
    {
        if (thing is NativeUnityEngineObject)
            return (thing as NativeUnityEngineObject).className ?? "MissingName";
        if (thing is ManagedObject)
            return (thing as ManagedObject).typeDescription.name;
        return thing.GetType().Name;
    }
    public static int GetCategory(ThingInMemory thing)
    {
        if (thing is NativeUnityEngineObject)
            return 1;
        if (thing is ManagedObject)
            return 2;

        return 3;
    }

    public static string GetCategoryLiteral(ThingInMemory thing)
    {
        if (thing is NativeUnityEngineObject)
            return "[native] ";
        if (thing is ManagedObject)
            return "[managed] ";
        if (thing is GCHandle)
            return "[gchandle] ";
        if (thing is StaticFields)
            return "[static] ";

        return "[unknown] ";
    }

    public static bool MatchSizeLimit(int size, int curLimitIndex)
    {
        if (curLimitIndex == 0)
            return true;

        switch (curLimitIndex)
        {
            case 0:
                return true;

            case 1:
                return size >= MemConst._1MB;

            case 2:
                return size >= MemConst._1KB && size < MemConst._1MB;

            case 3:
                return size < MemConst._1KB;

            default:
                return false;
        }
    }

    public static void LoadSnapshotProgress(float progress, string tag)
    {
        EditorUtility.DisplayProgressBar("Loading in progress, please wait...", string.Format("{0} - {1}%", tag, (int)(progress * 100.0f)), progress);

        if (progress >= 1.0f)
            EditorUtility.ClearProgressBar();
    }
}
