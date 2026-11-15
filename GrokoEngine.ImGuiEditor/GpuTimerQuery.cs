using System;
using OpenTK.Graphics.OpenGL4;

namespace GrokoEngine.ImGuiEditor;

/// <summary>
/// GPU timer query con triple buffer. Lee resultados de frames anteriores para evitar stalls.
/// Todas las llamadas deben hacerse desde el hilo propietario del contexto OpenGL.
/// </summary>
internal sealed class GpuTimerQuery : IDisposable
{
    private readonly int[] queries = new int[3];
    private int writeIndex;
    private bool running;
    private bool disposed;

    public double LastMilliseconds { get; private set; }
    public bool HasResult { get; private set; }

    public GpuTimerQuery()
    {
        GL.GenQueries(queries.Length, queries);
    }

    public void Begin()
    {
        if (disposed || running)
            return;

        TryReadAvailable();
        GL.BeginQuery(QueryTarget.TimeElapsed, queries[writeIndex]);
        running = true;
    }

    public void End()
    {
        if (disposed || !running)
            return;

        GL.EndQuery(QueryTarget.TimeElapsed);
        running = false;
        writeIndex = (writeIndex + 1) % queries.Length;
    }

    private void TryReadAvailable()
    {
        int readIndex = (writeIndex + 1) % queries.Length;
        GL.GetQueryObject(queries[readIndex], GetQueryObjectParam.QueryResultAvailable, out int available);
        if (available == 0)
            return;

        GL.GetQueryObject(queries[readIndex], GetQueryObjectParam.QueryResult, out long nanoseconds);
        LastMilliseconds = Math.Max(0d, nanoseconds / 1_000_000d);
        HasResult = true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        if (running)
        {
            GL.EndQuery(QueryTarget.TimeElapsed);
            running = false;
        }

        GL.DeleteQueries(queries.Length, queries);
        disposed = true;
    }
}
