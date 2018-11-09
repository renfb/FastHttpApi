﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BeetleX.Buffers;
using BeetleX.Clients;

namespace BeetleX.FastHttpApi.Clients
{
    public class HttpClientPacket : BeetleX.Clients.IClientPacket
    {
        public HttpClientPacket()
        {

        }

        private Response response;

        public EventClientPacketCompleted Completed { get; set; }

        public IClient Client { get; set; }

        public IClientPacket Clone()
        {
            if (pipeStream != null)
            {
                pipeStream.Dispose();
                pipeStream = null;
            }
            HttpClientPacket result = new HttpClientPacket();
            result.Client = this.Client;
            this.Client = null;
            return result;
        }

        private BeetleX.Buffers.PipeStream pipeStream;

        private int chunkeLength;

        private void loadChunkedData(PipeStream stream)
        {
        Next:
            string line;
            if (chunkeLength == 0)
            {
                if (!stream.TryReadWith(HeaderTypeFactory.LINE_BYTES, out line))
                    return;
                chunkeLength = int.Parse(line, System.Globalization.NumberStyles.HexNumber);
                if (chunkeLength == 0)
                {
                    stream.ReadFree(2);
                    var item = response;
                    pipeStream.Flush();
                    item.Stream = pipeStream;
                    response = null;
                    pipeStream = null;
                    Completed?.Invoke(Client, item);
                    return;
                }
                response.Length += chunkeLength;
            }
            else if (chunkeLength == -1)
            {
                if (stream.TryReadWith(HeaderTypeFactory.LINE_BYTES, out line))
                {
                    chunkeLength = 0;
                }
                else
                    return;
            }
            if (chunkeLength > 0)
            {
                if (pipeStream == null)
                    pipeStream = new PipeStream();
                while (true)
                {
                    byte[] buffer = HttpParse.GetByteBuffer();
                    int count = buffer.Length;
                    if (count > chunkeLength)
                        count = chunkeLength;
                    int read = stream.Read(buffer, 0, count);
                    if (read == 0)
                        return;
                    pipeStream.Write(buffer, 0, read);
                    chunkeLength -= read;
                    if (chunkeLength == 0)
                    {
                        chunkeLength = -1;
                        break;
                    }
                }
            }
            if (stream.Length > 0)
                goto Next;
        }

        public void Decode(IClient client, Stream stream)
        {
            var pipeStream = stream.ToPipeStream();
            if (response == null)
            {
                response = new Response();
            }
            if (response.Load(pipeStream) == LoadedState.Completed)
            {
                if (response.Chunked)
                {
                    loadChunkedData(pipeStream);
                }
                else
                {
                    if (response.Length == 0)
                    {
                        var item = response;
                        response = null;
                        Completed?.Invoke(Client, item);
                    }
                    else
                    {
                        if (response.Length == stream.Length)
                        {
                            var item = response;
                            item.Stream = pipeStream;
                            response = null;
                            Completed?.Invoke(Client, item);
                        }
                    }
                }
            }
        }

        private bool mIsDisposed = false;

        public void Dispose()
        {
            if (!mIsDisposed)
            {
                Client = null;
                mIsDisposed = true;
                if (pipeStream != null)
                {
                    pipeStream.Dispose();
                    pipeStream = null;
                }
            }
        }

        public void Encode(object data, IClient client, Stream stream)
        {
            Request request = (Request)data;
            request.Execute(stream.ToPipeStream());
        }
    }
}
