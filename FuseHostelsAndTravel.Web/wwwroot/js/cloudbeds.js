window.onload = function () {
    const params = new Proxy(new URLSearchParams(window.location.search), {
        get: (searchParams, prop) => searchParams.get(prop),
    });

    function setTextBoxVaues() {
        let firstName = params.firstName;
        let lastName = params.lastName;
        let email = params.email;
        let phone = params.phone;
        let country = params.country;

        if (firstName != null)
            $("#first_name").val(firstName).trigger('blur');

        if (lastName != null)
            $('#last_name').val(lastName).trigger('blur');

        if (email != null)
            $('#email').val(email).trigger('blur');

        if (phone != null)
            $('#phone').val(phone).trigger('blur');

        if (country != null)
            $('#country').val(country).trigger('blur');
    }

    window.setTimeout(function () {
        setTextBoxVaues();

        var element = document.querySelector('.page-md');
        var height = element.clientHeight;

        var messageObject = {
            data: height,
            target: "cloudbeds-booking-widget-iframe-size"
        }
        window.parent.postMessage(messageObject, "*");

        setInterval(function () {
            if (element.clientHeight != height) {
                height = element.clientHeight;
                var messageObject = {
                    data: height,
                    target: "cloudbeds-booking-widget-iframe-size"
                }
                window.parent.postMessage(messageObject, "*");

                setTextBoxVaues();
            }
        }, 200);
    }, 1000);
}