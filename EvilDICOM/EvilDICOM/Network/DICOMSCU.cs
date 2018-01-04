﻿#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using EvilDICOM.Network.DIMSE;
using EvilDICOM.Network.DIMSE.IOD;
using EvilDICOM.Network.Enums;
using EvilDICOM.Network.Messaging;
using EvilDICOM.Network.SCUOps;

#endregion

namespace EvilDICOM.Network
{
    public class DICOMSCU : DICOMServiceClass
    {
        public DICOMSCU(Entity ae) : base(ae)
        {
        }

        public void SendMessage(AbstractDIMSERequest dimse, Entity ae)
        {
            using (var client = new TcpClient())
            {
                client.ConnectAsync(IPAddress.Parse(ae.IpAddress), ae.Port).Wait();
                var assoc = new Association(this, client) { AeTitle = ae.AeTitle };
                PDataMessenger.Send(dimse, assoc);
                assoc.Listen();
            }
        }

        public CFinder GetCFinder(Entity callingEntity)
        {
            return new CFinder(this, callingEntity);
        }

        public CMover GetCMover(Entity callingEntity)
        {
            return new CMover(this, callingEntity);
        }

        public T GetResponse<T, U>(U request, Entity e, ref ushort msgId) where U : AbstractDIMSERequest where T : AbstractDIMSEResponse
        {
            System.DateTime lastContact = System.DateTime.Now;
            int msWait = 2000;

            var mr = new ManualResetEvent(false);
            T resp = null;
            var cr = new Services.DIMSEService.DIMSEResponseHandler<T>((res, asc) =>
            {
                lastContact = System.DateTime.Now;
                resp = res;
                if (res.Status != (ushort)Status.PENDING)
                    mr.Set();
                else { mr.Reset(); }
            });

            DIMSEService.Subscribe(cr);
            SendMessage(request, e);
            mr.WaitOne(msWait);
            DIMSEService.Unsubscribe(cr);
            msgId += 2;
            return resp;
        }

        public IEnumerable<T> GetResponses<T, U>(U request, Entity e, ref ushort msgId) where U : AbstractDIMSERequest where T : AbstractDIMSEResponse
        {
            System.DateTime lastContact = System.DateTime.Now;
            int msWait = 4000;

            var mr = new ManualResetEvent(false);

            List<T> responses = new List<T>();
            var cr = new Services.DIMSEService.DIMSEResponseHandler<T>((res, asc) =>
            {
                lastContact = System.DateTime.Now;
                Console.WriteLine($"Adding response {lastContact}");
                responses.Add(res);
                if (res.Status != (ushort)Status.PENDING)
                    mr.Set();
                else { mr.Reset(); }
            });

            DIMSEService.Subscribe(cr);
            SendMessage(request, e);
            mr.WaitOne(msWait); //Max wait for timeout
            DIMSEService.Unsubscribe(cr);
            msgId += 2;
            return responses;
        }

        /// <summary>
        /// Sends a CEcho request to the input entity
        /// </summary>
        /// <param name="ae">the entity to send the request</param>
        /// <param name="msTimeout">how long to wait in milliseconds before a timeout</param>
        /// <returns>true if success, false if failure</returns>
        public bool Ping(Entity ae, int msTimeout = 3000)
        {
            var responseSuccess = false;
            var ar = new AutoResetEvent(false);
            DIMSEService.CEchoResponseReceived += (res, asc) =>
            {
                responseSuccess = true;
                ar.Set();
            };
            SendMessage(new CEchoRequest(), ae);
            ar.WaitOne(msTimeout); //Give it 3 seconds
            return responseSuccess;
        }

        public CStoreRequest GenerateCStoreRequest(Core.DICOMObject toSend, ushort messageId = 1)
        {
            var sel = toSend.GetSelector();
            var cStoreReq = new CStoreRequest();
            cStoreReq.AffectedSOPClassUID = sel.SOPClassUID.Data;
            cStoreReq.Priority = 1;
            cStoreReq.Data = toSend;
            cStoreReq.MessageID = messageId;
            cStoreReq.AffectedSOPInstanceUID = sel.SOPInstanceUID.Data;
            cStoreReq.MoveOrigAETitle = ApplicationEntity.AeTitle;
            cStoreReq.MoveOrigMessageID = messageId;
            return cStoreReq;
        }

        /// <summary>
        /// Emits a CMove operation to an entity which moves an image from the entity to the specified AETitle
        /// </summary>
        /// <param name="scp">the provider which will perform the move</param>
        /// <param name="sopUid">the uid of the image to be moved</param>
        /// <param name="patientId">the patient id of the image</param>
        /// <param name="toAETite">the entity title which will receive the image</param>
        /// <param name="msgId">the message id</param>
        /// <returns>the move response</returns>
        public CGetResponse SendGetImage(Entity daemon, CFindImageIOD iod, ref ushort msgId)
        {
            var mr = new ManualResetEvent(false);
            CGetResponse resp = null;
            var cr = new Services.DIMSEService.DIMSEResponseHandler<CGetResponse>((res, asc) =>
            {
                if (!(res.Status == (ushort)Status.PENDING))
                    mr.Set();
                resp = res;
            });

            iod.QueryLevel = QueryLevel.IMAGE;

            var request = new CGetRequest(iod, Root.STUDY, Core.Enums.Priority.MEDIUM, msgId);
            DIMSEService.CGetResponseReceived += cr;
            SendMessage(request, daemon);
            mr.WaitOne();
            DIMSEService.CGetResponseReceived -= cr;
            msgId += 2;
            return resp;
        }
    }
}