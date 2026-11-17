using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Before1800
{
    // Explicit CLI verification of the Windows player; never enabled during normal play.
    public sealed class PrototypeSmokeRunner : MonoBehaviour
    {
        private PrototypeController c;
        private IEnumerator Start()
        {
            c=GetComponent<PrototypeController>();yield return null;
            if(c.Client.serviceUrl!="http://127.0.0.1:8001"){Finish(false,"fixture_url_required");yield break;}
            c.Begin();c.DiscoverCamera();c.OpenDialogue();c.View.Input.text="Please inspect the camera.";c.SendFromInput();
            float deadline=Time.realtimeSinceStartup+20;
            while(c.Waiting&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!c.State.HasPendingDecision){Finish(false,"first_request_failed: "+c.LastRawResponse);yield break;}
            c.ContinueDecision();deadline=Time.realtimeSinceStartup+40;
            while(c.State.HasActiveAction&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!c.State.Snapshot.verified_evidence.Contains("E1")){Finish(false,"E1_not_verified: "+c.cameraExperiment.Phase);yield break;}
            c.player.ResetPosition(c.lin.transform.position+Vector3.back*1.6f,0);
            c.OpenDialogue();c.View.Input.text="What did you actually observe?";c.SendFromInput();deadline=Time.realtimeSinceStartup+20;
            while(c.Waiting&&Time.realtimeSinceStartup<deadline)yield return null;
            bool passed=c.State.HasPendingDecision&&c.State.PendingDecision.action=="reply"&&c.LastRawResponse.Contains("TEST_FIXTURE");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath,"fixture-e1-feedback.png"));
            yield return new WaitForSecondsRealtime(.6f);
            Finish(passed,passed?"Two HTTP fixture rounds and actual A/B camera observation completed.":"second_request_failed");
        }
        private void Finish(bool pass,string reason)
        {
            var data=new {passed=pass,mode="TEST_FIXTURE_NOT_LLM",reason,state=c.State.Snapshot,raw_response=c.LastRawResponse,trace=c.Trace.FilePath};
            File.WriteAllText(Path.Combine(Application.persistentDataPath,"fixture-smoke-result.json"),JsonConvert.SerializeObject(data,Formatting.Indented));
            Application.Quit(pass?0:2);
        }
    }
}
