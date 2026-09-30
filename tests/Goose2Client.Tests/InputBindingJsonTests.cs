using System;
using System.Collections.Generic;
using System.Text;
using Goose2Client.InputBindings;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class InputBindingJsonTests
{
    [Fact]
    public void RoundTrip_PreservesAllFourBindingKindsAndAllModifiers()
    {
        var factory = FullSet();
        var active = FullSet(("Attack",
        [
            new InputBinding.Keyboard(Key.W, true, true, true, true),
            new InputBinding.Mouse(MouseButton.WheelUp, true, false, true, false),
            new InputBinding.JoypadButton(JoyButton.LeftShoulder),
            new InputBinding.JoypadAxis(JoyAxis.RightY, 1)
        ]));

        var result = InputBindingJson.Parse(InputBindingJson.Serialize(active, factory));

        Assert.True(result.Success, result.Error);
        Assert.Equal(active.GetBindings("Attack"), result.Overrides!["Attack"]);
        Assert.Single(result.Overrides);
    }

    [Fact]
    public void RoundTrip_PreservesAxisDirectionSigns()
    {
        var factory = FullSet();
        var active = FullSet(
            ("MoveLeft", [new InputBinding.JoypadAxis(JoyAxis.LeftX, -1)]),
            ("MoveRight", [new InputBinding.JoypadAxis(JoyAxis.LeftX, 1)]));

        var result = InputBindingJson.Parse(InputBindingJson.Serialize(active, factory));

        Assert.True(result.Success, result.Error);
        Assert.Equal(active.GetBindings("MoveLeft"), result.Overrides!["MoveLeft"]);
        Assert.Equal(active.GetBindings("MoveRight"), result.Overrides!["MoveRight"]);
    }

    [Fact]
    public void RoundTrip_PresentEmptyActionList_StaysEmpty()
    {
        var factory = FullSet(("Attack", [new InputBinding.Keyboard(Key.F, false, false, false, false)]));
        var active = FullSet();

        var result = InputBindingJson.Parse(InputBindingJson.Serialize(active, factory));

        Assert.True(result.Success, result.Error);
        Assert.True(result.Overrides!.ContainsKey("Attack"));
        Assert.Empty(result.Overrides["Attack"]);
    }

    [Fact]
    public void Parse_MissingAction_IsAbsentFromOverrides_DistinctFromPresentEmpty()
    {
        var missing = InputBindingJson.Parse("{\"version\": 1, \"actions\": {}}");
        Assert.True(missing.Success, missing.Error);
        Assert.Empty(missing.Overrides!);
        Assert.False(missing.Overrides!.ContainsKey("Attack"));

        var present = InputBindingJson.Parse("{\"version\": 1, \"actions\": {\"Attack\": []}}");
        Assert.True(present.Success, present.Error);
        Assert.True(present.Overrides!.ContainsKey("Attack"));
        Assert.Empty(present.Overrides!["Attack"]);
    }

    [Fact]
    public void Serialize_OmitsActionsEqualToFactory()
    {
        var shared = new[] { new InputBinding.Keyboard(Key.G, false, false, false, false) };
        var factory = FullSet(("Attack", shared));
        var active = FullSet(
            ("Attack", shared),
            ("PickUp", [new InputBinding.Mouse(MouseButton.Middle, false, false, false, false)]));

        var json = Encoding.UTF8.GetString(InputBindingJson.Serialize(active, factory));

        Assert.DoesNotContain("\"Attack\"", json);
        Assert.Contains("\"PickUp\"", json);

        var result = InputBindingJson.Parse(json);
        Assert.True(result.Success, result.Error);
        Assert.False(result.Overrides!.ContainsKey("Attack"));
        Assert.Single(result.Overrides);
    }

    [Fact]
    public void Serialize_ListOrderIsSignificantAndPreserved()
    {
        var factory = FullSet();
        var key = new InputBinding.Keyboard(Key.A, false, false, false, false);
        var mouse = new InputBinding.Mouse(MouseButton.Middle, false, false, false, false);

        var forward = InputBindingJson.Serialize(FullSet(("Attack", [key, mouse])), factory);
        var reversed = InputBindingJson.Serialize(FullSet(("Attack", [mouse, key])), factory);

        Assert.NotEqual(forward, reversed);

        var result = InputBindingJson.Parse(forward);
        Assert.True(result.Success, result.Error);
        Assert.Equal(new InputBinding[] { key, mouse }, result.Overrides!["Attack"]);
    }

    [Fact]
    public void Serialize_IsDeterministic_ByteForByte()
    {
        var factory = FullSet();
        var active = FullSet(("Attack", [new InputBinding.Keyboard(Key.W, true, false, false, false)]));

        Assert.Equal(InputBindingJson.Serialize(active, factory), InputBindingJson.Serialize(active, factory));
    }

    [Fact]
    public void Serialize_EmitsExactSchemaWithFixedPropertyOrder()
    {
        var factory = FullSet();
        var active = FullSet(("MoveUp",
        [
            new InputBinding.Keyboard(Key.W, false, false, false, false),
            new InputBinding.Mouse(MouseButton.Middle, false, false, false, false),
            new InputBinding.JoypadButton(JoyButton.A),
            new InputBinding.JoypadAxis(JoyAxis.LeftY, -1)
        ]));

        var expected = """
            {
              "version": 1,
              "actions": {
                "MoveUp": [
                  {
                    "kind": "key",
                    "physicalKey": 87,
                    "ctrl": false,
                    "shift": false,
                    "alt": false,
                    "meta": false
                  },
                  {
                    "kind": "mouse",
                    "button": 3,
                    "ctrl": false,
                    "shift": false,
                    "alt": false,
                    "meta": false
                  },
                  {
                    "kind": "joyButton",
                    "button": 0
                  },
                  {
                    "kind": "joyAxis",
                    "axis": 1,
                    "direction": -1
                  }
                ]
              }
            }
            """;

        Assert.Equal(Encoding.UTF8.GetBytes(expected), InputBindingJson.Serialize(active, factory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"version\": 1, \"actions\": {}} trailing")]
    [InlineData("[\"version\"]")]
    [InlineData("null")]
    public void Parse_MalformedOrTrailingJson_Fails(string json)
    {
        var result = InputBindingJson.Parse(json);
        Assert.False(result.Success);
        Assert.Null(result.Overrides);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\": 1}")]
    [InlineData("{\"actions\": {}}")]
    [InlineData("{\"version\": 1, \"actions\": {}, \"extra\": true}")]
    [InlineData("{\"version\": 1, \"version\": 2, \"actions\": {}}")]
    [InlineData("{\"actions\": {}, \"version\": 1}")]
    public void Parse_InvalidRootShape_Fails(string json)
    {
        var result = InputBindingJson.Parse(json);
        Assert.False(result.Success);
        Assert.Null(result.Overrides);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void Parse_UnsupportedVersion_Fails(int version)
    {
        var result = InputBindingJson.Parse($"{{\"version\": {version}, \"actions\": {{}}}}");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    [InlineData("true")]
    public void Parse_NonIntegerVersion_Fails(string raw)
    {
        var result = InputBindingJson.Parse($"{{\"version\": {raw}, \"actions\": {{}}}}");
        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_DuplicateActionProperty_Fails()
    {
        var result = InputBindingJson.Parse("{\"version\": 1, \"actions\": {\"Attack\": [], \"Attack\": []}}");
        Assert.False(result.Success);
        Assert.Null(result.Overrides);
    }

    [Fact]
    public void Parse_UnknownActionName_Fails()
    {
        var result = InputBindingJson.Parse("{\"version\": 1, \"actions\": {\"Fly\": []}}");
        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_UnknownBindingKind_Fails()
    {
        var result = InputBindingJson.Parse("{\"version\": 1, \"actions\": {\"Attack\": [{\"kind\": \"pen\", \"tip\": 1}]}}");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("{\"kind\":\"key\",\"physicalKey\":87,\"shift\":false,\"alt\":false,\"meta\":false}")]
    [InlineData("{\"kind\":\"key\",\"physicalKey\":87,\"ctrl\":false,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}")]
    [InlineData("{\"kind\":\"key\",\"physicalKey\":87,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false,\"extra\":true}")]
    [InlineData("{\"kind\":\"key\",\"shift\":false,\"physicalKey\":87,\"alt\":false,\"meta\":false,\"ctrl\":false}")]
    [InlineData("{\"kind\":\"key\",\"physicalKey\":87}")]
    [InlineData("{\"kind\":\"mouse\",\"button\":3,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false,\"extra\":0}")]
    [InlineData("{\"kind\":\"joyButton\",\"button\":0,\"ctrl\":false}")]
    [InlineData("{\"kind\":\"joyAxis\",\"axis\":1}")]
    [InlineData("{\"kind\":\"joyAxis\",\"direction\":1,\"axis\":1}")]
    public void Parse_InvalidBindingProperties_Fails(string binding)
    {
        var result = InputBindingJson.Parse($"{{\"version\": 1, \"actions\": {{\"Attack\": [{binding}]}}}}");
        Assert.False(result.Success);
        Assert.Null(result.Overrides);
    }

    [Theory]
    [InlineData("{\"kind\":\"key\",\"physicalKey\":\"W\",\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}")]
    [InlineData("{\"kind\":\"key\",\"physicalKey\":87.5,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}")]
    [InlineData("{\"kind\":\"key\",\"physicalKey\":87,\"ctrl\":1,\"shift\":false,\"alt\":false,\"meta\":false}")]
    [InlineData("{\"kind\":5,\"physicalKey\":87,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}")]
    [InlineData("87")]
    [InlineData("\"key\"")]
    public void Parse_WrongBindingTypes_Fails(string binding)
    {
        var result = InputBindingJson.Parse($"{{\"version\": 1, \"actions\": {{\"Attack\": [{binding}]}}}}");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99999)]
    [InlineData(268435473)]
    [InlineData((long)Key.Ctrl)]
    [InlineData((long)Key.Shift)]
    [InlineData((long)Key.Alt)]
    [InlineData((long)Key.Meta)]
    [InlineData(9223372036854775806L)]
    public void Parse_Keyboard_OutOfRangeOrReservedKeys_Fails(long physicalKey)
    {
        var result = InputBindingJson.Parse(
            $"{{\"version\": 1, \"actions\": {{\"Attack\": [{{\"kind\":\"key\",\"physicalKey\":{physicalKey},\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}}]}}}}");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData((int)Key.A)]
    [InlineData((int)Key.W)]
    [InlineData((int)Key.Escape)]
    public void Parse_Keyboard_ValidKeys_AreAccepted(int physicalKey)
    {
        var result = InputBindingJson.Parse(
            $"{{\"version\": 1, \"actions\": {{\"Attack\": [{{\"kind\":\"key\",\"physicalKey\":{physicalKey},\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}}]}}}}");
        Assert.True(result.Success, result.Error);
    }

    [Theory]
    [InlineData((int)MouseButton.None)]
    [InlineData((int)MouseButton.Left)]
    [InlineData((int)MouseButton.Right)]
    [InlineData(999)]
    public void Parse_Mouse_ReservedOrOutOfRangeButtons_Fails(int button)
    {
        var result = InputBindingJson.Parse(
            $"{{\"version\": 1, \"actions\": {{\"Attack\": [{{\"kind\":\"mouse\",\"button\":{button},\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}}]}}}}");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData((int)JoyButton.Invalid)]
    [InlineData((int)JoyButton.Max)]
    public void Parse_JoypadButton_OutOfRangeButtons_Fails(int button)
    {
        var result = InputBindingJson.Parse($"{{\"version\": 1, \"actions\": {{\"Attack\": [{{\"kind\":\"joyButton\",\"button\":{button}}}]}}}}");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData((int)JoyAxis.Invalid)]
    [InlineData((int)JoyAxis.Max)]
    public void Parse_JoypadAxis_OutOfRangeAxes_Fails(int axis)
    {
        var result = InputBindingJson.Parse($"{{\"version\": 1, \"actions\": {{\"Attack\": [{{\"kind\":\"joyAxis\",\"axis\":{axis},\"direction\":1}}]}}}}");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    [InlineData(100)]
    public void Parse_JoypadAxis_InvalidDirection_Fails(int direction)
    {
        var result = InputBindingJson.Parse(
            $"{{\"version\": 1, \"actions\": {{\"Attack\": [{{\"kind\":\"joyAxis\",\"axis\":1,\"direction\":{direction}}}]}}}}");
        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_DuplicateBindingsInList_Fails()
    {
        var binding = "{\"kind\":\"key\",\"physicalKey\":87,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}";
        var result = InputBindingJson.Parse($"{{\"version\": 1, \"actions\": {{\"Attack\": [{binding}, {binding}]}}}}");
        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_MixedValidAndInvalidActions_FailsWithoutPartialResult()
    {
        var valid = "{\"kind\":\"key\",\"physicalKey\":87,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}";
        var invalid = "{\"kind\":\"key\",\"physicalKey\":0,\"ctrl\":false,\"shift\":false,\"alt\":false,\"meta\":false}";
        var result = InputBindingJson.Parse(
            $"{{\"version\": 1, \"actions\": {{\"Attack\": [{valid}], \"PickUp\": [{invalid}]}}}}");

        Assert.False(result.Success);
        Assert.Null(result.Overrides);
    }

    [Fact]
    public void Parse_ActionValueMustBeArray_Fails()
    {
        var result = InputBindingJson.Parse("{\"version\": 1, \"actions\": {\"Attack\": {}}}");
        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_ActionsMustBeObject_Fails()
    {
        var result = InputBindingJson.Parse("{\"version\": 1, \"actions\": []}");
        Assert.False(result.Success);
    }

    private static InputBindingSet FullSet(params (string Name, InputBinding[] Bindings)[] overrides)
    {
        var bindings = new Dictionary<string, IReadOnlyList<InputBinding>>();
        foreach (var action in InputActionCatalog.Actions)
            bindings[action.Name] = Array.Empty<InputBinding>();
        foreach (var (name, list) in overrides)
            bindings[name] = list;
        return new InputBindingSet(bindings);
    }
}
