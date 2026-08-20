using UnityEngine;
using JocyfCloudsToy;
using System.Collections;

[RequireComponent(typeof(CloudsToy))]
public class SingleCloudSetup : MonoBehaviour
{
    [Header("Single Cloud Settings")]
    [Tooltip("Particle size multiplier (CloudsToy SizeFactorPart)")]
    [Range(0.1f, 10f)]
    public float sizeMultiplier = 1f;

    [Tooltip("Cloud particle detail level")]
    public CloudsToy.TypeDetail cloudDetail = CloudsToy.TypeDetail.Normal;

    [Tooltip("Cloud texture type")]
    public CloudsToy.TypeCloud cloudType = CloudsToy.TypeCloud.Nimbus2;

    [Tooltip("Render mode")]
    public CloudsToy.TypeRender cloudRender = CloudsToy.TypeRender.Realistic;

    [Header("Cloud Dimensions")]
    [Tooltip("Max width of the cloud ellipsoid (>20 = Big)")]
    [Range(10, 200)]
    public int maxWidth = 100;

    [Tooltip("Max height of the cloud ellipsoid")]
    [Range(5, 200)]
    public int maxHeight = 40;

    [Tooltip("Max depth of the cloud ellipsoid")]
    [Range(5, 200)]
    public int maxDepth = 100;

    CloudsToy _ct;

    void Awake()
    {
        _ct = GetComponent<CloudsToy>();
        if (!_ct) return;

        _ct.MaximunClouds = 1;
        _ct.NumberClouds = 1;

        _ct.MaximunVelocity = Vector3.zero;
        _ct.VelocityMultipier = 0f;

        _ct.Side = new Vector3(0.01f, 0.01f, 0.01f);

        _ct.FixedSize = true;
        _ct.MaxWidthCloud = maxWidth;
        _ct.MaxHeightCloud = maxHeight;
        _ct.MaxDepthCloud = maxDepth;

        _ct.SizeFactorPart = sizeMultiplier;
        _ct.CloudDetail = cloudDetail;
        _ct.TypeClouds = cloudType;
        _ct.CloudRender = cloudRender;

        _ct.NumberOfShadows = CloudsToy.TypeShadow.None;
    }
}
