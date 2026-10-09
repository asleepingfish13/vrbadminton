using System;
using UnityEngine;

namespace ViveShuttleVR.Editor
{
    public static class RacketSpecificationRegression
    {
        private static int checks;

        public static int Run()
        {
            checks=0;
            TestPublishedAndEstimatedProperties();
            TestImpactCoupling();
            TestGeometry();
            Debug.Log("[Racket Specification Test] PASS "+checks+" checks: official weight/tension, estimated grip inertia, passive contact, isometric geometry and mesh bounds.");
            return checks;
        }

        private static void Require(bool passed,string message)
        {
            checks++;
            if (!passed) throw new InvalidOperationException("[Racket Specification Test] "+message);
        }

        private static void Near(float actual,float expected,float tolerance,string message)
        {
            Require(Mathf.Abs(actual-expected)<=tolerance,message+" actual="+actual+" expected="+expected);
        }

        private static void TestPublishedAndEstimatedProperties()
        {
            RacketSpecification four=new RacketSpecification();
            RacketSpecification three=new RacketSpecification { weightClass=RacketWeightClass.ThreeU };
            Near(four.NominalUnstrungMass,.083f,.000001f,"4U published average mass");
            Near(three.NominalUnstrungMass,.088f,.000001f,"3U published average mass");
            Near(four.RecommendedTensionMin,20f,.001f,"4U minimum tension");
            Near(four.RecommendedTensionMax,28f,.001f,"4U maximum tension");
            Near(three.RecommendedTensionMin,21f,.001f,"3U minimum tension");
            Near(three.RecommendedTensionMax,29f,.001f,"3U maximum tension");
            Near(RacketSpecification.PublishedLengthExtension,.01f,.000001f,"Published length extension");
            Require(RacketSpecification.AppearanceReferenceUrl.Contains("AX100ZZ_554-1"),"Dark Navy appearance must retain its official photo reference");
            Require(RacketSpecification.DarkNavyColor.maxColorComponent<.25f,"Dark Navy must remain dark, not a bright toy-colour frame");
            Require(RacketSpecification.OrangeAccentColor.r>RacketSpecification.OrangeAccentColor.b*3f,"Official Dark Navy lower-right accent is orange");
            Require(four.TensionNotice(25f).StartsWith("Within"),"Default 25 lb should be recommended");
            Require(four.TensionNotice(29f).StartsWith("SANDBOX"),"Out-of-range tension must not look manufacturer-approved");
            Require(four.EstimatedPlayingMass>four.NominalUnstrungMass,"Added strings/overgrip are separate from frame mass");
            Require(three.TransverseGripInertia>four.TransverseGripInertia,"Same estimated mass distribution should increase 3U inertia");
            float center=four.EffectiveContactMass(Vector3.zero,Vector3.forward);
            Near(center,four.TransverseGripInertia/(RacketSpecification.HeadCenter-RacketSpecification.GripCenter).sqrMagnitude,.000001f,"Central effective mass must use I/r^2");
            Require(center<four.NominalUnstrungMass,"A grip-pivot effective mass is not the whole frame mass");
            Require(four.EffectiveContactMass(new Vector3(.075f,0f,0f),Vector3.forward)<center,"Lateral offset must include twist inertia");
            Require(three.EffectiveContactMass(Vector3.zero,Vector3.forward)>center,"3U should increase contact mass");
            Near(four.NormalizedFaceRadius(Vector2.right*RacketSpecification.HeadHalfWidth),1f,.000001f,"String-bed lateral extent");
            Near(four.NormalizedFaceRadius(Vector2.up*RacketSpecification.HeadHalfHeight),1f,.000001f,"String-bed longitudinal extent");
            for (int i=0;i<72;i++)
            {
                Vector3 point=RacketGeometry.Outline(i*5f*Mathf.Deg2Rad,RacketSpecification.HeadHalfWidth,RacketSpecification.HeadHalfHeight);
                Near(four.NormalizedFaceRadius(new Vector2(point.x,point.y)),1f,.00001f,"Rendered and physical isometric boundary agree");
            }
        }

        private static RacketContact Contact(Vector3 position,Vector3 velocity)
        {
            return new RacketContact { point=position,center=Vector3.zero,centerVelocity=velocity,
                faceRotation=Quaternion.identity,surfaceNormal=Vector3.forward };
        }

        private static void TestImpactCoupling()
        {
            ShuttlePhysicsSettings four=new ShuttlePhysicsSettings { seatedAssist=false };
            ShuttlePhysicsSettings three=new ShuttlePhysicsSettings { seatedAssist=false,racketWeightClass=RacketWeightClass.ThreeU };
            ShuttleState shuttle=ShuttleState.Launch(Vector3.zero,Vector3.back*4f);
            RacketContact contact=Contact(Vector3.zero,Vector3.forward*8f);
            ImpactResult a=RacketImpact.Resolve(shuttle,contact,four), b=RacketImpact.Resolve(shuttle,contact,three);
            Require(a.hit&&b.hit,"Both racket mass classes must resolve a centered incoming shuttle");
            Require(b.effectiveRacketMass>a.effectiveRacketMass,"Resolver must actually use the selected weight class");
            Require(a.outgoingVelocity.z>0f&&b.outgoingVelocity.z>a.outgoingVelocity.z,"3U changes physical output at equal measured swing; direction stays forward");
            float previous=0f;
            for (int speed=2;speed<=22;speed+=2)
            {
                ImpactResult shot=RacketImpact.Resolve(shuttle,Contact(Vector3.zero,Vector3.forward*speed),four);
                Require(shot.hit&&shot.outgoingVelocity.z>previous,"Increasing measured swing speed must increase forward output");
                previous=shot.outgoingVelocity.z;
            }
            // The explicit geometry side rejects separating contact, preserving
            // the forward-swing/backward-flight regression fix.
            ShuttleState leaving=ShuttleState.Launch(Vector3.zero,Vector3.forward*9f);
            Require(!RacketImpact.Resolve(leaving,contact,four).hit,"Never flip a known geometry-side normal to manufacture a hit");
            foreach (RacketWeightClass weight in new[] { RacketWeightClass.FourU,RacketWeightClass.ThreeU })
                foreach (float tension in new[] { RacketSpecification.MinTension(weight),25f,RacketSpecification.MaxTension(weight) })
                    foreach (float offset in new[] { 0f,.04f,.075f })
                    {
                        ShuttlePhysicsSettings setting=new ShuttlePhysicsSettings { seatedAssist=false,racketWeightClass=weight,stringTensionLbs=tension };
                        ShuttleState incoming=ShuttleState.Launch(Vector3.zero,new Vector3(2f,-1f,-14f));
                        incoming.angularVelocity=new Vector3(20f,-35f,10f);
                        ImpactResult hit=RacketImpact.Resolve(incoming,Contact(Vector3.right*offset,Vector3.zero),setting);
                        Require(hit.hit,"Passive profile sweep should contact");
                        float initial=Energy(incoming.velocity,incoming.angularVelocity,incoming.orientation*Vector3.up,setting);
                        float final=Energy(hit.outgoingVelocity,hit.angularVelocity,incoming.orientation*Vector3.up,setting);
                        Require(final<=initial+.00001f,"A stationary 4U/3U racket must not create kinetic energy");
                        Require(hit.effectiveRacketMass>=.02f&&hit.effectiveRacketMass<=.2f,"Estimated contact mass must remain finite and bounded");
                    }
        }

        private static float Energy(Vector3 velocity,Vector3 spin,Vector3 tail,ShuttlePhysicsSettings settings)
        {
            float axial=Vector3.Dot(spin,tail);
            return .5f*settings.mass*velocity.sqrMagnitude+.5f*settings.InertiaScale*
                (ShuttlePhysics.AxialInertia*axial*axial+ShuttlePhysics.TransverseInertia*(spin-tail*axial).sqrMagnitude);
        }

        private static void TestGeometry()
        {
            GameObject root=new GameObject("Racket geometry verification");
            Material material=new Material(Shader.Find("Standard"));
            try
            {
                Transform head=RacketGeometry.Build(root.transform,material,material,material,material,material,material,material);
                Near((head.localPosition-RacketSpecification.HeadCenter).magnitude,0f,.000001f,"Grip lever must use rendered head center");
                MeshFilter[] meshes=root.GetComponentsInChildren<MeshFilter>();
                Require(meshes.Length==7,"Detailed geometry must remain seven material-combined meshes");
                Require(root.GetComponentsInChildren<Collider>().Length==0,"Racket visuals must not create duplicate Unity collisions");
                Require(root.transform.Find("Blue swept frame and shaft graphics")!=null,"Blue frame/shaft graphics missing");
                Require(root.transform.Find("Orange lower-right frame and cone markings")!=null,"Distinctive asymmetric orange accent missing");
                Require(root.transform.Find("Black grip grommets and frame legend")!=null,"Grip and physical grommets missing");
                Require(root.transform.Find("Red stencil printed only on strings")!=null,"Reference-photo string stencil missing");
                float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
                int totalVertices=0,totalTriangles=0;
                foreach (MeshFilter filter in meshes)
                {
                    Mesh mesh=filter.sharedMesh;
                    totalVertices+=mesh.vertexCount;
                    Require(mesh!=null&&mesh.vertexCount>0,"Every geometry mesh must exist");
                    Require(mesh.vertexCount<65000,"All generated meshes fit 16-bit indices");
                    foreach (Vector3 point in mesh.vertices)
                    {
                        Vector3 local=root.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                        minY=Mathf.Min(minY,local.y); maxY=Mathf.Max(maxY,local.y);
                    }
                    int[] triangles=mesh.triangles;
                    totalTriangles+=triangles.Length/3;
                    Require(triangles.Length>0&&triangles.Length%3==0,"Triangle topology must be valid");
                    for (int i=0;i<triangles.Length;i++)
                        if (triangles[i]<0||triangles[i]>=mesh.vertexCount)
                            throw new InvalidOperationException("Invalid generated racket index");
                }
                Require(totalVertices<=16000&&totalTriangles<=22000,"Detailed racket exceeds the intended lightweight VR mesh budget");
                Near(maxY-minY,RacketSpecification.EstimatedLength,.001f,"Model must be approximately 675 mm, not the old oversized hoop");
            }
            finally
            {
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                    UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
