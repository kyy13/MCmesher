// MCmesher
// Kyle J Burgess

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;

public class MCmesher : IDisposable
{
    public MCmesher()
    {
        m_meshBuffer = McmCreateMeshBuffer();
    }

    public void Dispose()
    {
        TryDispose();
        GC.SuppressFinalize(this);
    }

    // Generate a procedural mesh (with 3D array)
    // Pass an existing meshFilter.mesh into GenerateMesh as a reference,
    // avoid using Mesh.Clear() and creating a new Mesh(), because GenerateMesh() is designed to
    // handle overwriting existing meshes.
    // voxelData       the scalara data field of the marching cubes mesh
    // dataSize        size of scalar field data (points)
    // meshOrigin      mesh origin in scalar field (cubes)
    // meshSize        mesh size in scalar field (cubes)
    // isoLevel        voxelData value > isoLevel means it's inside the surface
    // vertexNormals   true means use iterpolated vertex normals, false means use face normals
    public void GenerateMesh(
        ref Mesh mesh,
        float[,,] voxelData,
        Vector3u dataSize,
        Vector3u meshOrigin,
        Vector3u meshSize,
        float isoLevel = 0.5f,
        bool vertexNormals = true)
    {
        McmResult r;

        if (vertexNormals)
        {
            r = McmGenerateMeshVN(m_meshBuffer, voxelData, dataSize, meshOrigin, meshSize, isoLevel);
        }
        else
        {
            r = McmGenerateMeshFN(m_meshBuffer, voxelData, dataSize, meshOrigin, meshSize, isoLevel);
        }

        if (r != McmResult.MCM_SUCCESS)
        {
            MeshResultToException(r);
        }

        ApplyMeshToMeshFilter(ref mesh, dataSize);
    }

    // Generate a procedural mesh (with 1D array of size = width * height * depth)
    // see GenerateMesh() 3D array above for details
    public void GenerateMesh(
        ref Mesh mesh,
        float[] voxelData,
        Vector3u dataSize,
        Vector3u meshOrigin,
        Vector3u meshSize,
        float isoLevel = 0.5f,
        bool vertexNormals = true)
    {
        McmResult r;

        if (vertexNormals)
        {
            r = McmGenerateMeshVN(m_meshBuffer, voxelData, dataSize, meshOrigin, meshSize, isoLevel);
        }
        else
        {
            r = McmGenerateMeshFN(m_meshBuffer, voxelData, dataSize, meshOrigin, meshSize, isoLevel);
        }

        if (r != McmResult.MCM_SUCCESS)
        {
            MeshResultToException(r);
        }

        ApplyMeshToMeshFilter(ref mesh, dataSize);
    }

    // Intersect a scalar field with a ray (gives the same results as mesh-ray intersection except faster, and the mesh does not need to be generated)
    // If an intersection occurs, then returns the point of intersection, otherwise returns null
    public static Vector3? RayIntersectVirtualMesh(float[] data, Vector3u dataSize, float isoLevel, Vector3 rayPos, Vector3 rayDir)
    {
        Vector3 pIntersect;

        if (McmRayIntersectVirtualMesh(data, dataSize, isoLevel, rayPos, rayDir, out pIntersect) == McmResult.MCM_SUCCESS)
        {
            return pIntersect;
        }

        return null;
    }

    // RayIntersectVirtualMesh (with 1D array of size = width * height * depth)
    // see RayIntersectVirtualMesh() 3D array above for details
    public static Vector3? RayIntersectVirtualMesh(float[,,] data, Vector3u dataSize, float isoLevel, Vector3 rayPos, Vector3 rayDir)
    {
        Vector3 pIntersect;

        if (McmRayIntersectVirtualMesh(data, dataSize, isoLevel, rayPos, rayDir, out pIntersect) == McmResult.MCM_SUCCESS)
        {
            return pIntersect;
        }

        return null;
    }

    protected void MeshResultToException(McmResult r)
    {
        switch(r)
        {
        case McmResult.MCM_MESH_BUFFER_IS_NULL:
            throw new NullReferenceException();
        case McmResult.MCM_OUT_OF_BOUNDS_X:
        case McmResult.MCM_OUT_OF_BOUNDS_Y:
        case McmResult.MCM_OUT_OF_BOUNDS_Z:
            throw new ArgumentOutOfRangeException();
        default:
            throw new Exception();
        };
    }

    protected void ApplyMeshToMeshFilter(ref Mesh mesh, Vector3u boundsMaxU)
    {
        var dataArray = Mesh.AllocateWritableMeshData(1);
        var data = dataArray[0];

        int vertexCount = (int)McmCountVertices(m_meshBuffer);
        int indexCount = (int)McmCountIndices(m_meshBuffer);

        data.SetVertexBufferParams(vertexCount,
            new VertexAttributeDescriptor(VertexAttribute.Position),
            new VertexAttributeDescriptor(VertexAttribute.Normal, stream: 1));

        var vertices = data.GetVertexData<Vector3>();

        unsafe
        {
            McmCopyVertices(m_meshBuffer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(vertices));
        }

        var normals = data.GetVertexData<Vector3>(1);

        unsafe
        {
            McmCopyNormals(m_meshBuffer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(normals));
        }

        data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

        var indices = data.GetIndexData<uint>();

        unsafe
        {
            McmCopyIndices(m_meshBuffer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(indices));
        }

        Vector3 boundsMax = new Vector3(
            (float)(boundsMaxU.x - 1),
            (float)(boundsMaxU.y - 1),
            (float)(boundsMaxU.z - 1));

        var subMeshDescriptor = new SubMeshDescriptor(0, indexCount);

        subMeshDescriptor.bounds.SetMinMax(Vector3.zero, boundsMax);
        subMeshDescriptor.firstVertex = 0;
        subMeshDescriptor.vertexCount = indexCount;

        data.subMeshCount = 1;
        data.SetSubMesh(0, subMeshDescriptor, MeshUpdateFlags.DontRecalculateBounds);

        Mesh.ApplyAndDisposeWritableMeshData(dataArray, mesh);

        mesh.RecalculateBounds();
    }

    ~MCmesher()
    {
        TryDispose();
    }

    protected virtual void TryDispose()
    {
        if (!m_disposed)
        {
            McmDeleteMeshBuffer(m_meshBuffer);
            m_disposed = true;
        }
    }

    protected IntPtr m_meshBuffer;

    protected bool m_disposed = false;

    // -------------------------------------------
    // MCmesher C++ Library Methods and Structs
    // -------------------------------------------

    // Vector3 of uint
    [StructLayout(LayoutKind.Explicit)]
    public struct Vector3u
    {
        [FieldOffset(0)] public uint x;
        [FieldOffset(4)] public uint y;
        [FieldOffset(8)] public uint z;
    }

    // Result of MCmesher functions
    protected enum McmResult
    {
        MCM_SUCCESS                         = 0,            // Function was successful
        MCM_MESH_BUFFER_IS_NULL             = 1,            // Error, the mesh buffer passed to the function was NULL
        MCM_OUT_OF_BOUNDS_X                 = 2,            // Error, an argument passed to the function was out of bounds in the x-axis
        MCM_OUT_OF_BOUNDS_Y                 = 3,            // Error, an argument passed to the function was out of bounds in the y-axis
        MCM_OUT_OF_BOUNDS_Z                 = 4,            // Error, an argument passed to the function was out of bounds in the z-axis
        MCM_NO_INTERSECTION                 = 5,            // Returned by mcmRayIntersectMesh when there is no intersection
    };

    // Create an McmMeshBuffer and return its handle (pointer)
    [DllImport("libMCmesher", EntryPoint = "mcmCreateMeshBuffer", CallingConvention = CallingConvention.Cdecl)]
    protected static extern IntPtr      McmCreateMeshBuffer();

    // Delete an McmMeshBuffer, invalidating the handle
    [DllImport("libMCmesher", EntryPoint = "mcmDeleteMeshBuffer", CallingConvention = CallingConvention.Cdecl)]
    protected static extern void        McmDeleteMeshBuffer(
        IntPtr                              meshBuffer);    // Handle to a valid mcmMeshBuffer object

    // Generate a marching cubes mesh with vertex normals, and store the results in an McmMeshBuffer
    [DllImport("libMCmesher", EntryPoint = "mcmGenerateMeshVN", CallingConvention = CallingConvention.Cdecl)]
    protected static extern McmResult   McmGenerateMeshVN(
        IntPtr                              meshBuffer,     // Handle to a valid mcmMeshBuffer object
        float[]                             data,           // 3D field of scalar floating-point values as a contiguous array
        Vector3u                            dataSize,       // Size of 3D field x, y, and z axis (in vertices) where field array length is x * y * z
        Vector3u                            meshOrigin,     // Origin of the mesh to generate (in cubes) within the 3D field
        Vector3u                            meshSize,       // Size of the mesh to generate (in cubes) within the 3D field
        float                               isoLevel);      // The ISO level for the surface (under ISO = inside the volume, over ISO = outside the volume)

    // Generate a marching cubes mesh with vertex normals, and store the results in an McmMeshBuffer
    [DllImport("libMCmesher", EntryPoint = "mcmGenerateMeshVN", CallingConvention = CallingConvention.Cdecl)]
    protected static extern McmResult   McmGenerateMeshVN(
        IntPtr                              meshBuffer,     // Handle to a valid mcmMeshBuffer object
        float[,,]                           data,           // 3D field of scalar floating-point values as a contiguous array
        Vector3u                            dataSize,       // Size of 3D field x, y, and z axis (in vertices) where field array length is x * y * z
        Vector3u                            meshOrigin,     // Origin of the mesh to generate (in cubes) within the 3D field
        Vector3u                            meshSize,       // Size of the mesh to generate (in cubes) within the 3D field
        float                               isoLevel);      // The ISO level for the surface (under ISO = inside the volume, over ISO = outside the volume)

    // Generate a marching cubes mesh with face normals, and store the results in an McmMeshBuffer
    [DllImport("libMCmesher", EntryPoint = "mcmGenerateMeshFN", CallingConvention = CallingConvention.Cdecl)]
    protected static extern McmResult   McmGenerateMeshFN(
        IntPtr                              meshBuffer,     // Handle to a valid mcmMeshBuffer object
        float[]                             data,           // 3D field of scalar floating-point values as a contiguous array
        Vector3u                            dataSize,       // Size of 3D field x, y, and z axis (in vertices) where field array length is x * y * z
        Vector3u                            meshOrigin,     // Origin of the mesh to generate (in cubes) within the 3D field
        Vector3u                            meshSize,       // Size of the mesh to generate (in cubes) within the 3D field
        float                               isoLevel);      // The ISO level for the surface (under ISO = inside the volume, over ISO = outside the volume)

    // Generate a marching cubes mesh with vertex normals, and store the results in an McmMeshBuffer
    [DllImport("libMCmesher", EntryPoint = "mcmGenerateMeshFN", CallingConvention = CallingConvention.Cdecl)]
    protected static extern McmResult   McmGenerateMeshFN(
        IntPtr                              meshBuffer,     // Handle to a valid mcmMeshBuffer object
        float[,,]                           data,           // 3D field of scalar floating-point values as a contiguous array
        Vector3u                            dataSize,       // Size of 3D field x, y, and z axis (in vertices) where field array length is x * y * z
        Vector3u                            meshOrigin,     // Origin of the mesh to generate (in cubes) within the 3D field
        Vector3u                            meshSize,       // Size of the mesh to generate (in cubes) within the 3D field
        float                               isoLevel);      // The ISO level for the surface (under ISO = inside the volume, over ISO = outside the volume)

    // Intersect a scalar field with a ray (gives the same results as mesh-ray intersection except faster, and the mesh does not need to be generated)
    // If an intersection occurs, then mcmRayIntersectMesh returns MCM_SUCCESS, and the point of intersection is set,
    // otherwise, mcmRayIntersectMesh returns MCM_NO_INTERSECTION
    [DllImport("libMCmesher", EntryPoint = "mcmRayIntersectVirtualMesh", CallingConvention = CallingConvention.Cdecl)]
    protected static extern McmResult   McmRayIntersectVirtualMesh(
        float[]                             data,           // 3D field of scalar floating-point values as a contiguous array
        Vector3u                            dataSize,       // Size of 3D field x, y, and z axis (in vertices) where field array length is x * y * z
        float                               isoLevel,       // The ISO level for the surface (under ISO = inside the volume, over ISO = outside the volume)
        Vector3                             rayPos,         // Starting point of the ray
        Vector3                             rayDir,         // Direction of the ray (does not need to be normalized)
        out Vector3                         pIntersect);    // The point of intersection if an intersection occurred

    // Intersect a scalar field with a ray (gives the same results as mesh-ray intersection except faster, and the mesh does not need to be generated)
    // If an intersection occurs, then mcmRayIntersectMesh returns MCM_SUCCESS, and the point of intersection is set,
    // otherwise, mcmRayIntersectMesh returns MCM_NO_INTERSECTION
    [DllImport("libMCmesher", EntryPoint = "mcmRayIntersectVirtualMesh", CallingConvention = CallingConvention.Cdecl)]
    protected static extern McmResult   McmRayIntersectVirtualMesh(
        float[,,]                           data,           // 3D field of scalar floating-point values as a contiguous array
        Vector3u                            dataSize,       // Size of 3D field x, y, and z axis (in vertices) where field array length is x * y * z
        float                               isoLevel,       // The ISO level for the surface (under ISO = inside the volume, over ISO = outside the volume)
        Vector3                             rayPos,         // Starting point of the ray
        Vector3                             rayDir,         // Direction of the ray (does not need to be normalized)
        out Vector3                         pIntersect);    // The point of intersection if an intersection occurred

    // Count the number of vertices in the mesh
    [DllImport("libMCmesher", EntryPoint = "mcmCountVertices", CallingConvention = CallingConvention.Cdecl)]
    protected static extern uint        McmCountVertices(
        IntPtr                              meshBuffer);    // Handle to a valid mcmMeshBuffer object

    // Copies vertices into a buffer
    [DllImport("libMCmesher", EntryPoint = "mcmCopyVertices", CallingConvention = CallingConvention.Cdecl)]
    protected static extern unsafe void McmCopyVertices(
        IntPtr                              meshBuffer,     // Handle to a valid mcmMeshBuffer object
        void*                               dstBuffer);     // Destination buffer of Vector3<float> (allocated size must be >= mcmCountVertices(meshBuffer))

    // Count the number of normals in the mesh
    [DllImport("libMCmesher", EntryPoint = "mcmCountNormals", CallingConvention = CallingConvention.Cdecl)]
    protected static extern uint        McmCountNormals(
        IntPtr                              meshBuffer);    // Handle to a valid mcmMeshBuffer object

    // Copies normals into a buffer
    [DllImport("libMCmesher", EntryPoint = "mcmCopyNormals", CallingConvention = CallingConvention.Cdecl)]
    protected static extern unsafe void McmCopyNormals(
        IntPtr                              meshBuffer,     // Handle to a valid mcmMeshBuffer object
        void*                               dstBuffer);     // Destination buffer of Vector3<float> (allocated size must be >= mcmCountNormals(meshBuffer))

    // Count the number of indices in the mesh
    [DllImport("libMCmesher", EntryPoint = "mcmCountIndices", CallingConvention = CallingConvention.Cdecl)]
    protected static extern uint        McmCountIndices(
        IntPtr                              meshBuffer);    // Handle to a valid mcmMeshBuffer object

    // Copies indices into a buffer
    [DllImport("libMCmesher", EntryPoint = "mcmCopyIndices", CallingConvention = CallingConvention.Cdecl)]
    protected static extern unsafe void McmCopyIndices(
        IntPtr                              meshBuffer,     // Handle to a valid mcmMeshBuffer object
        void*                               dstBuffer);     // Destination buffer of unsigned 32-bit integers (allocated size must be >= mcmCountIndices(meshBuffer))
}