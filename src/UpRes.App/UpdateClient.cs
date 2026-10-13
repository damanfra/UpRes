using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UpRes.Core;

namespace UpRes.App;

/// <summary>
/// Consulta o último Release no GitHub (somente leitura, só quando o usuário clica).
/// Nada é baixado nem instalado.
/// </summary>
internal static class UpdateClient
{
    public static Task<UpdateResult> CheckAsync() => Task.Run(Check);

    private static UpdateResult Check()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(UpdateCheck.LatestReleaseApi);
            req.Method = "GET";
            req.UserAgent = "UpRes/" + BuildInfo.Version; // a API do GitHub exige User-Agent
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials; // proxy corporativo

            using var resp = (HttpWebResponse)req.GetResponse();
            using var reader = new StreamReader(resp.GetResponseStream()!);
            var release = UpdateCheck.ParseRelease(reader.ReadToEnd());
            return UpdateCheck.Compare(BuildInfo.Version, release);
        }
        catch (WebException ex) when (ex.Response is HttpWebResponse r && r.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateResult { Status = UpdateStatus.NoReleases, Message = "Ainda não há nenhuma versão publicada no GitHub." };
        }
        catch (Exception ex)
        {
            return new UpdateResult
            {
                Status = UpdateStatus.Unknown,
                Message = "Não foi possível consultar o GitHub (" + ex.Message + "). Verifique a conexão ou o proxy, ou abra " + UpdateCheck.ReleasesPage,
            };
        }
    }
}
