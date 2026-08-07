using System;
using System.Text.Json;
using SvitloSk.Publisher.Domain.Contracts;
using Xunit;

namespace SvitloSk.Publisher.Domain.Tests.Contracts;

public class GraphicPackageDeserializationTests
{
    [Fact]
    public void Should_Deserialize_Valid_Graphic_JSON_To_DTO()
    {
        string json = @"
        {
          ""metadata"": {
            ""package_id"": ""b8f8b67b-2275-4d7a-8fbb-5a4c9a87353a"",
            ""generation_timestamp"": ""2026-08-06T15:00:00Z"",
            ""target_date"": ""2026-08-07"",
            ""source_identifier"": ""dso-graphic-v1""
          },
          ""territorial_scope"": ""hmel"",
          ""queues"": [
            {
              ""queue_id"": ""q1"",
              ""subqueues"": [
                {
                  ""subqueue_id"": ""q1.1"",
                  ""intervals"": [
                    {
                      ""start_time"": ""08:00"",
                      ""end_time"": ""12:00"",
                      ""status"": ""RESTRICTED""
                    }
                  ]
                }
              ]
            }
          ]
        }";

        var dto = JsonSerializer.Deserialize<GraphicInputPackageDto>(json);
        
        Assert.NotNull(dto);
        Assert.Equal(Guid.Parse("b8f8b67b-2275-4d7a-8fbb-5a4c9a87353a"), dto.Metadata.PackageId);
        Assert.Equal(new DateOnly(2026, 8, 7), dto.Metadata.TargetDate);
        Assert.Equal("hmel", dto.TerritorialScope);
        Assert.Single(dto.Queues);
        Assert.Equal("q1", dto.Queues[0].QueueId);
        Assert.Single(dto.Queues[0].Subqueues);
        Assert.Equal("q1.1", dto.Queues[0].Subqueues[0].SubqueueId);
        Assert.Single(dto.Queues[0].Subqueues[0].Intervals);
        Assert.Equal("08:00", dto.Queues[0].Subqueues[0].Intervals[0].StartTime);
        Assert.Equal("12:00", dto.Queues[0].Subqueues[0].Intervals[0].EndTime);
        Assert.Equal("RESTRICTED", dto.Queues[0].Subqueues[0].Intervals[0].Status);
    }
}
