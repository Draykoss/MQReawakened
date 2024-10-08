﻿namespace Server.Base.Core.Extensions;

public static class GetFile
{
    public static FileStream GetFileStream(string fileName, string internalDir, FileMode mode)
    {
        var path = Path.Combine(InternalDirectory.GetBaseDirectory(), internalDir);

        InternalDirectory.CreateDirectory(path);

        var currentLog = Path.Combine(path, fileName);

        return File.Open(currentLog, mode);
    }

    public static StreamWriter GetStreamWriter(string fileName, string internalDir, FileMode mode) =>
        new(GetFileStream(fileName, internalDir, mode))
        {
            AutoFlush = false
        };
}
