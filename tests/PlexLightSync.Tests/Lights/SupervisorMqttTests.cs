using PlexLightSync.Lights;

namespace PlexLightSync.Tests.Lights;

public sealed class SupervisorMqttTests
{
    [Fact]
    public void The_supervisor_reply_becomes_the_mqtt_settings()
    {
        // shaped like the supervisor's GET /services/mqtt reply, with made-up values
        const string reply = """
            {"result":"ok","data":{"host":"core-mosquitto","port":1883,"ssl":false,"protocol":"3.1.1",
             "username":"addons","password":"secret","app":"core_mosquitto"}}
            """;

        var settings = SupervisorMqtt.Parse(reply);

        Assert.Equal("core-mosquitto", settings["mqtt_host"]);
        Assert.Equal("1883", settings["mqtt_port"]);
        Assert.Equal("addons", settings["mqtt_user"]);
        Assert.Equal("secret", settings["mqtt_password"]);
    }

    [Fact]
    public void A_broker_that_requires_ssl_is_refused_with_a_clear_reason()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SupervisorMqtt.Parse("""{"result":"ok","data":{"host":"core-mosquitto","port":8883,"ssl":true}}"""));

        Assert.Contains("ssl", error.Message);
    }

    [Fact]
    public void A_reply_without_data_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => SupervisorMqtt.Parse("""{"result":"error","message":"No access"}"""));
    }
}
